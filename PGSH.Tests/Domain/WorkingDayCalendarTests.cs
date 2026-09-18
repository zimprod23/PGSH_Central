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

    /// <summary>A férié the faculty works through: it counts toward a duration but still bounds nothing.</summary>
    private static Holiday Worked(int year, int month, int day, string name = "Férié travaillé")
    {
        var holiday = Day(year, month, day, name);
        holiday.CountsAsWorkingDay = true;
        return holiday;
    }

    [Fact]
    public void A_worked_holiday_counts_toward_a_duration_but_may_not_bound_a_window()
    {
        // Thursday 1 January 2026, declared worked. The whole point of the flag: the day is served, so it
        // costs the window nothing — and a période still must not begin or end on it.
        var calendar = WorkingDayCalendar.Build([Worked(2026, 1, 1, "Nouvel An")]);

        calendar.CountsTowardDuration(new DateOnly(2026, 1, 1)).Should().BeTrue();
        calendar.CanBoundAWindow(new DateOnly(2026, 1, 1)).Should().BeFalse();

        // Monday 29 December 2025 → Friday 2 January 2026 is five worked days, exactly as if nothing had
        // been declared. Unflagged, the same holiday would make it four.
        var week = (From: new DateOnly(2025, 12, 29), To: new DateOnly(2026, 1, 2));

        calendar.Count(week.From, week.To).Should().Be(5);
        WorkingDayCalendar.Build([Day(2026, 1, 1)]).Count(week.From, week.To).Should().Be(4);
    }

    [Fact]
    public void Every_bounding_day_counts_but_not_every_counted_day_bounds()
    {
        // The predicates are nested, never independent — the property the whole split rests on. A day
        // that could bound a window and did not count would be a window whose own last day is not in it.
        var calendar = WorkingDayCalendar.Build(
        [
            Worked(2026, 1, 1, "Nouvel An"),
            Day(2026, 1, 12, "Manifeste de l'Indépendance"),
            Span(new DateOnly(2026, 3, 19), new DateOnly(2026, 3, 21), "Aïd al-Fitr"),
        ]);

        for (var day = new DateOnly(2025, 12, 1); day <= new DateOnly(2026, 6, 30); day = day.AddDays(1))
            if (calendar.CanBoundAWindow(day))
                calendar.CountsTowardDuration(day).Should().BeTrue($"{day:yyyy-MM-dd} bounds a window");
    }

    [Fact]
    public void A_window_whose_count_runs_out_on_a_worked_holiday_ends_on_the_next_day_that_can_bound_it()
    {
        // This is the case the flag creates and the one that had to be decided rather than discovered on a
        // published table. Thursday 1 January 2026 is worked, so it is the 4ᵗʰ counted day of a window
        // opening Monday 29 December — but nothing may end on it.
        var calendar = WorkingDayCalendar.Build([Worked(2026, 1, 1, "Nouvel An")]);

        var window = calendar.Lay(new DateOnly(2025, 12, 29), 4);

        window.Should().NotBeNull();
        window!.Start.Should().Be(new DateOnly(2025, 12, 29));
        window.End.Should().Be(new DateOnly(2026, 1, 2), "the 1ᵉʳ is served but cannot close a période");
        calendar.CanBoundAWindow(window.End).Should().BeTrue();

        // It therefore holds one day more than was asked for, and says so rather than leaving a reader to
        // discover that one column of an axis is wider than its neighbours.
        window.RequestedWorkingDays.Should().Be(4);
        window.WorkingDays.Should().Be(5);
        window.RunsLongerThanAsked.Should().BeTrue();
    }

    [Fact]
    public void What_a_window_reports_holding_is_what_recounting_it_gives()
    {
        // The invariant that decided the shape above. Extending the end without counting what the
        // extension crosses would have left Count() and WorkingDays disagreeing about one window — one
        // number standing for two facts, which is the defect class this codebase is measured against.
        var calendar = WorkingDayCalendar.Build(
        [
            Worked(2026, 1, 1, "Nouvel An"),
            Worked(2026, 1, 2, "Lendemain, travaillé aussi"),
            Day(2026, 1, 20, "Férié chômé"),
        ]);

        foreach (int asked in Enumerable.Range(1, 30))
        {
            var window = calendar.Lay(new DateOnly(2025, 12, 22), asked);

            window.Should().NotBeNull();
            window!.WorkingDays.Should().Be(calendar.Count(window.Start, window.End),
                $"a window laid for {asked} day(s) must hold what recounting it gives");
            window.WorkingDays.Should().BeGreaterThanOrEqualTo(asked);
            calendar.CanBoundAWindow(window.Start).Should().BeTrue();
            calendar.CanBoundAWindow(window.End).Should().BeTrue();
        }
    }

    [Fact]
    public void A_worked_holiday_costs_a_series_nothing()
    {
        // The reason the flag exists at all: eight fériés fall inside 2026-2027 and each one lengthens
        // every column that crosses it. Flagged, the columns are laid as if the holiday were not there.
        // Tuesday 1 December 2026, mid-column on a six-column axis opening Monday 2 November.
        var date = new DateOnly(2026, 12, 1);
        var worked = WorkingDayCalendar.Build([Worked(date.Year, date.Month, date.Day, "Journée déclarée")]);
        var closed = WorkingDayCalendar.Build([Day(date.Year, date.Month, date.Day, "Journée déclarée")]);
        var bare = WorkingDayCalendar.WeekendsOnly();

        var start = new DateOnly(2026, 11, 2);

        worked.LaySeries(start, 6, 15).Select(w => w.End).Should()
            .Equal(bare.LaySeries(start, 6, 15).Select(w => w.End),
                "a day the faculty works through changes no column's end");

        // The control: unflagged, the same row pushes every column from the second one onward.
        closed.LaySeries(start, 6, 15).Select(w => w.End).Should()
            .NotEqual(bare.LaySeries(start, 6, 15).Select(w => w.End));
    }

    [Fact]
    public void A_pause_can_never_be_worked_through_however_it_is_proposed()
    {
        // ProposedClosure exists so a preview cannot report what the act it previews is unable to be, and
        // PromotionPause answers false unconditionally. Asking for the opposite is simply not granted.
        var proposal = new ProposedClosure(
            new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15), "Examens", true,
            CalendarClosureScope.Promotion, WorkedThrough: true);

        proposal.CountsAsWorkingDay.Should().BeFalse();

        new ProposedClosure(
            new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15), "Vacances", true,
            CalendarClosureScope.Faculty, WorkedThrough: true)
            .CountsAsWorkingDay.Should().BeTrue();
    }

    [Fact]
    public void Weekends_do_not_count()
    {
        var calendar = WorkingDayCalendar.WeekendsOnly();

        // Monday 1 September 2025 → Sunday 7 September: five worked days.
        calendar.Count(new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 7)).Should().Be(5);
        calendar.CountsTowardDuration(new DateOnly(2025, 9, 6)).Should().BeFalse();
        calendar.CountsTowardDuration(new DateOnly(2025, 9, 8)).Should().BeTrue();

        // A rest day is the one case where the two questions agree: nobody serves it and nothing may end
        // on it. Everything that follows is about the cases where they do not.
        calendar.CanBoundAWindow(new DateOnly(2025, 9, 6)).Should().BeFalse();
        calendar.CanBoundAWindow(new DateOnly(2025, 9, 8)).Should().BeTrue();
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
            calendar.CanBoundAWindow(window.Start).Should().BeTrue();
            calendar.CanBoundAWindow(window.End).Should().BeTrue();
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
