using FluentAssertions;
using PGSH.Domain.Calendar;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// Jours ouvrables. The calendar is pure, so the awkward cases — a window opening on a Saturday, a stage
/// straddling Aïd, a holiday that costs nothing because it lands on a Sunday — are settled here rather than
/// argued about over a wrong end date on the published table.
/// </summary>
public class WorkingDayCalendarTests
{
    private static Holiday Day(int year, int month, int day, string name = "Férié",
        HolidayKind kind = HolidayKind.National, bool confirmed = true) =>
        new()
        {
            StartDate = new DateOnly(year, month, day),
            EndDate = new DateOnly(year, month, day),
            Name = name,
            Kind = kind,
            IsConfirmed = confirmed,
        };

    private static Holiday Span(DateOnly from, DateOnly to, string name, bool confirmed = true) =>
        new() { StartDate = from, EndDate = to, Name = name, Kind = HolidayKind.Religious, IsConfirmed = confirmed };

    [Fact]
    public void Weekends_do_not_count()
    {
        var calendar = WorkingDayCalendar.WeekendsOnly();

        // Monday 1 September 2025 → Sunday 7 September: five worked days.
        calendar.Count(new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 7)).Should().Be(5);
        calendar.IsWorkingDay(new DateOnly(2025, 9, 6)).Should().BeFalse();
        calendar.IsWorkingDay(new DateOnly(2025, 9, 8)).Should().BeTrue();
    }

    [Fact]
    public void A_holiday_falling_on_a_weekend_costs_nothing()
    {
        // 1 November 2025 is a Saturday. Declaring a holiday on it removes no worked day — which is why
        // "days lost" is reported against the weekend-only calendar rather than assumed to be DayCount.
        var withHoliday = WorkingDayCalendar.Build([Day(2025, 11, 1)]);
        var without = WorkingDayCalendar.WeekendsOnly();

        var from = new DateOnly(2025, 10, 27);
        var to = new DateOnly(2025, 11, 7);

        withHoliday.Count(from, to).Should().Be(without.Count(from, to));
    }

    [Fact]
    public void A_multi_day_holiday_removes_only_its_working_days()
    {
        // Aïd al-Adha over Fri 5 – Sat 6 June 2026: Friday is worked, Saturday is not.
        var calendar = WorkingDayCalendar.Build(
            [Span(new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 6), "Aïd al-Adha")]);

        var week = (From: new DateOnly(2026, 6, 1), To: new DateOnly(2026, 6, 7));

        WorkingDayCalendar.WeekendsOnly().Count(week.From, week.To).Should().Be(5);
        calendar.Count(week.From, week.To).Should().Be(4);
    }

    [Fact]
    public void A_window_asked_to_open_on_a_rest_day_opens_on_the_next_worked_one()
    {
        // Saturday 5 September 2026. A window whose first day nobody attends misreports its own length.
        var calendar = WorkingDayCalendar.WeekendsOnly();

        var window = calendar.Lay(new DateOnly(2026, 9, 5), 10);

        window.Should().NotBeNull();
        window!.Start.Should().Be(new DateOnly(2026, 9, 7));
        window.Start.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void A_window_always_ends_on_a_worked_day_so_two_windows_never_share_a_weekend()
    {
        var calendar = WorkingDayCalendar.WeekendsOnly();

        var windows = calendar.LaySeries(new DateOnly(2025, 9, 1), 4, 20);

        windows.Should().HaveCount(4);

        foreach (var window in windows)
        {
            calendar.IsWorkingDay(window.Start).Should().BeTrue();
            calendar.IsWorkingDay(window.End).Should().BeTrue();
            window.WorkingDays.Should().Be(20);
        }

        // Contiguous and non-overlapping: the trailing weekend belongs to neither window.
        for (int i = 1; i < windows.Count; i++)
            windows[i].Start.Should().BeAfter(windows[i - 1].End);
    }

    [Fact]
    public void Every_column_of_a_series_holds_the_same_number_of_worked_days()
    {
        // The property that calendar months cannot give: février and mars are not the same amount of
        // stage, and this is the unit under which they are.
        var calendar = WorkingDayCalendar.Build(
        [
            Day(2026, 1, 1, "Nouvel An"),
            Day(2026, 1, 11, "Manifeste de l'Indépendance"),
            Span(new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 21), "Aïd al-Fitr"),
            Day(2026, 5, 1, "Fête du Travail"),
        ]);

        var windows = calendar.LaySeries(new DateOnly(2025, 12, 1), 6, 22);

        windows.Should().HaveCount(6);
        windows.Select(w => w.WorkingDays).Distinct().Should().Equal([22]);

        // …and it pays for that with an uneven wall-calendar length, which is the trade being made.
        windows.Select(w => w.CalendarDays).Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public void A_window_reports_the_holidays_it_swallowed_and_whether_they_are_settled()
    {
        var calendar = WorkingDayCalendar.Build(
        [
            Day(2026, 1, 11, "Manifeste de l'Indépendance"),
            Span(new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 20), "Aïd al-Mawlid", confirmed: false),
        ]);

        var window = calendar.Lay(new DateOnly(2026, 1, 5), 20);

        window!.HolidaysHit.Select(h => h.Name).Should()
            .BeEquivalentTo(["Manifeste de l'Indépendance", "Aïd al-Mawlid"]);

        // A lunar date can move by a day in either direction, so a window laid over one is a window that
        // may have to be reprinted.
        window.HasProvisionalDates.Should().BeTrue();
    }

    [Fact]
    public void An_unusable_working_week_lays_nothing_rather_than_spinning()
    {
        var everyDayOff = new WorkingWeek(Enum.GetValues<DayOfWeek>().ToHashSet());
        var calendar = WorkingDayCalendar.WeekendsOnly(everyDayOff);

        calendar.Lay(new DateOnly(2025, 9, 1), 5).Should().BeNull();
        calendar.LaySeries(new DateOnly(2025, 9, 1), 3, 5).Should().BeEmpty();
    }

    [Fact]
    public void A_non_positive_length_lays_nothing()
    {
        var calendar = WorkingDayCalendar.WeekendsOnly();

        calendar.Lay(new DateOnly(2025, 9, 1), 0).Should().BeNull();
        calendar.Count(new DateOnly(2025, 9, 30), new DateOnly(2025, 9, 1)).Should().Be(0);
    }

    [Fact]
    public void Fixed_national_holidays_are_generated_and_the_Amazigh_new_year_only_from_2024()
    {
        var before = MoroccanPublicHolidays.FixedFor(2023);
        var after = MoroccanPublicHolidays.FixedFor(2024);

        // Décret of May 2023, first observed 2024 — generating it earlier would invent a day off that
        // was worked.
        before.Should().NotContain(h => h.Name == "Nouvel An Amazigh");
        after.Should().Contain(h => h.Name == "Nouvel An Amazigh"
                                 && h.StartDate == new DateOnly(2024, 1, 14));

        after.Should().OnlyContain(h => h.Kind == HolidayKind.National && h.IsConfirmed);
        after.Should().OnlyContain(h => h.StartDate == h.EndDate);
        after.Select(h => h.StartDate).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// The whole span matters, not the queried one. A lunar date moves ~11 days a year, so "is it
    /// recorded?" only has a stable answer over the whole Gregorian year — a narrow window would report
    /// holidays missing that are sitting on file a few months away.
    /// </summary>
    [Fact]
    public void Missing_religious_is_answered_over_the_whole_gregorian_year()
    {
        var calendar = WorkingDayCalendar.Build(
        [
            Span(new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 21), "Aïd al-Fitr"),
            // August, so outside a 1 September – 31 July academic year — and still not "missing".
            Span(new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 26), "Aïd al-Mawlid"),
        ]);

        var missing = calendar.MissingReligious(new DateOnly(2025, 9, 1), new DateOnly(2026, 7, 31));

        missing.Should().NotContain("Aïd al-Fitr");
        missing.Should().NotContain("Aïd al-Mawlid");
        missing.Should().BeEquivalentTo(["Aïd al-Adha", "1ᵉʳ Moharram"]);
    }

    [Fact]
    public void The_religious_holidays_are_named_but_never_dated()
    {
        // The whole point: PGSH cannot compute them. It can only say which ones a complete year needs.
        MoroccanPublicHolidays.ExpectedReligious.Should().HaveCountGreaterThan(0);
        MoroccanPublicHolidays.ExpectedReligious.Should().OnlyContain(e => e.UsualDayCount >= 1);

        MoroccanPublicHolidays.FixedFor(2026)
            .Should().NotContain(h => h.Kind == HolidayKind.Religious);
    }
}
