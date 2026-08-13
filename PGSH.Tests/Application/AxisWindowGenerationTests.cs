using FluentAssertions;
using PGSH.Application.Calendar;
using PGSH.Application.Stages.RotationCycle;
using PGSH.Domain.Calendar;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Laying a block's axis from one start date. The whole reason it is a server call is the holiday table: the
/// browser used to do this with <c>setUTCMonth</c>, which is right for calendar months and wrong the moment
/// a duration is stated in jours ouvrables.
/// </summary>
public class AxisWindowGenerationTests
{
    private static GenerateAxisWindowsQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new WorkingDayProvider(db));

    [Fact]
    public async Task Monthly_columns_are_contiguous_and_inclusive_of_both_ends()
    {
        await using var db = TestHarness.NewContext(nameof(Monthly_columns_are_contiguous_and_inclusive_of_both_ends));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(4, new DateOnly(2025, 10, 1), AxisColumnUnit.Months), default);

        result.IsSuccess.Should().BeTrue();
        var columns = result.Value.Columns;

        columns.Should().HaveCount(4);
        columns[0].StartDate.Should().Be(new DateOnly(2025, 10, 1));
        columns[0].EndDate.Should().Be(new DateOnly(2025, 10, 31));
        columns[3].EndDate.Should().Be(new DateOnly(2026, 1, 31));

        // The convention SlotOverlapGuard enforces: the next column starts the day after the last ends.
        for (int i = 1; i < columns.Count; i++)
            columns[i].StartDate.Should().Be(columns[i - 1].EndDate.AddDays(1));

        columns.Select(c => c.Number).Should().Equal([1, 2, 3, 4]);
    }

    [Theory]
    [InlineData(1, 7)]
    [InlineData(2, 14)]
    [InlineData(3, 21)]
    [InlineData(4, 28)]
    public async Task A_column_can_be_any_number_of_weeks(int weeks, int expectedCalendarDays)
    {
        await using var db = TestHarness.NewContext(
            $"{nameof(A_column_can_be_any_number_of_weeks)}_{weeks}");
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(3, new DateOnly(2025, 10, 6), AxisColumnUnit.Weeks, weeks),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Columns.Should().OnlyContain(c => c.CalendarDays == expectedCalendarDays);

        // Whole weeks from a Monday, so every column holds the same five days per week and the count is
        // exact without the working-day unit being needed.
        result.Value.Columns.Should().OnlyContain(c => c.WorkingDays == weeks * 5);
    }

    [Fact]
    public async Task Working_day_columns_all_hold_the_same_amount_of_stage()
    {
        await using var db = TestHarness.NewContext(nameof(Working_day_columns_all_hold_the_same_amount_of_stage));
        db.SeedCatalog();
        db.SeedHoliday(new DateOnly(2026, 1, 1), "Nouvel An");
        db.SeedHoliday(new DateOnly(2026, 1, 12), "Manifeste de l'Indépendance");
        db.SeedHoliday(new DateOnly(2026, 3, 19), "Aïd al-Fitr", days: 2, kind: HolidayKind.Religious);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(6, new DateOnly(2025, 12, 1), AxisColumnUnit.WorkingDays, 20),
            default);

        result.IsSuccess.Should().BeTrue();

        result.Value.Columns.Select(c => c.WorkingDays).Distinct().Should().Equal([20]);
        result.Value.WorkingDaysTotal.Should().Be(120);

        // The trade: equal stage, unequal wall-calendar length.
        result.Value.Columns.Select(c => c.CalendarDays).Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task A_column_names_the_holidays_it_swallowed()
    {
        await using var db = TestHarness.NewContext(nameof(A_column_names_the_holidays_it_swallowed));
        db.SeedCatalog();
        db.SeedHoliday(new DateOnly(2025, 11, 6), "Marche Verte");
        db.SeedHoliday(new DateOnly(2025, 11, 18), "Fête de l'Indépendance");
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(3, new DateOnly(2025, 10, 1), AxisColumnUnit.Months), default);

        var november = result.Value.Columns.Single(c => c.StartDate.Month == 11);

        november.Holidays.Should().BeEquivalentTo(["Marche Verte", "Fête de l'Indépendance"]);
        november.WorkingDays.Should().Be(18);   // 20 weekdays in November 2025, minus two fériés
    }

    /// <summary>
    /// On a fresh base nothing is recorded, so « jours ouvrables » quietly means "minus weekends" — a
    /// narrower thing than it says. Reported rather than left to be discovered from a short stage.
    /// </summary>
    [Fact]
    public async Task An_empty_calendar_is_flagged_rather_than_silently_counting_weekends_only()
    {
        await using var db = TestHarness.NewContext(nameof(An_empty_calendar_is_flagged_rather_than_silently_counting_weekends_only));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(2, new DateOnly(2025, 10, 1), AxisColumnUnit.WorkingDays, 20),
            default);

        result.Value.CalendarIsEmpty.Should().BeTrue();
        result.Value.MissingReligious.Should().Contain("Aïd al-Fitr");
    }

    /// <summary>
    /// A Hijri date drifts about eleven days earlier each year, so a lunar holiday lands anywhere in the
    /// Gregorian calendar. Asking "is Aïd recorded?" of the axis span answers a different question: an
    /// autumn block would report every spring holiday missing and send the user hunting for rows already
    /// on file. The check is deliberately widened to the whole Gregorian years the axis touches.
    /// </summary>
    [Fact]
    public async Task A_spring_holiday_is_not_reported_missing_by_an_autumn_axis()
    {
        await using var db = TestHarness.NewContext(nameof(A_spring_holiday_is_not_reported_missing_by_an_autumn_axis));
        db.SeedCatalog();
        db.SeedHoliday(new DateOnly(2026, 3, 19), "Aïd al-Fitr", days: 2, kind: HolidayKind.Religious);
        db.SeedHoliday(new DateOnly(2026, 5, 27), "Aïd al-Adha", days: 2, kind: HolidayKind.Religious);
        await db.SaveChangesAsync();

        // Four monthly columns over October 2025 – January 2026: nowhere near either Aïd.
        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(4, new DateOnly(2025, 10, 1), AxisColumnUnit.Months), default);

        result.Value.MissingReligious.Should().NotContain("Aïd al-Fitr");
        result.Value.MissingReligious.Should().NotContain("Aïd al-Adha");

        // Still catches what genuinely is not on file.
        result.Value.MissingReligious.Should().Contain("Aïd al-Mawlid");
    }

    [Fact]
    public async Task A_provisional_lunar_date_inside_a_column_is_warned_about()
    {
        await using var db = TestHarness.NewContext(nameof(A_provisional_lunar_date_inside_a_column_is_warned_about));
        db.SeedCatalog();
        db.SeedHoliday(new DateOnly(2026, 3, 19), "Aïd al-Fitr", days: 2,
            kind: HolidayKind.Religious, confirmed: false);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(2, new DateOnly(2026, 3, 1), AxisColumnUnit.Months), default);

        result.Value.Columns[0].HasProvisionalDates.Should().BeTrue();
        result.Value.Warnings.Should().Contain(w => w.Contains("provisoire"));
    }

    /// <summary>
    /// Février and août are not the same amount of stage. Under <see cref="AxisColumnUnit.Months"/> that is a
    /// fact about calendars rather than a defect, so it is a warning naming the working-day unit as the fix.
    /// </summary>
    [Fact]
    public async Task Monthly_columns_of_unequal_working_length_are_warned_about()
    {
        await using var db = TestHarness.NewContext(nameof(Monthly_columns_of_unequal_working_length_are_warned_about));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(6, new DateOnly(2026, 2, 1), AxisColumnUnit.Months), default);

        var spread = result.Value.Columns.Max(c => c.WorkingDays)
                   - result.Value.Columns.Min(c => c.WorkingDays);

        spread.Should().BeGreaterThanOrEqualTo(3);
        result.Value.Warnings.Should().Contain(w => w.Contains("jours ouvrables"));
    }

    [Fact]
    public async Task Working_day_columns_are_never_warned_about_for_spread()
    {
        await using var db = TestHarness.NewContext(nameof(Working_day_columns_are_never_warned_about_for_spread));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(6, new DateOnly(2026, 2, 2), AxisColumnUnit.WorkingDays, 20),
            default);

        // Fixed by construction, so a spread here would be a bug rather than a fact about calendars.
        result.Value.Warnings.Should().NotContain(w => w.Contains("d'écart"));
    }

    [Fact]
    public async Task An_axis_running_past_the_academic_year_is_warned_about()
    {
        await using var db = TestHarness.NewContext(nameof(An_axis_running_past_the_academic_year_is_warned_about));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(12, new DateOnly(2026, 6, 1), AxisColumnUnit.Months), default);

        result.Value.Warnings.Should().Contain(w => w.Contains("année universitaire"));
    }

    /// <summary>
    /// A « vacances » span typed with the wrong year can swallow the horizon. That is a data-entry accident,
    /// not a planning mistake, so the refusal points at the calendar.
    /// </summary>
    [Fact]
    public async Task A_calendar_with_no_working_days_left_refuses_instead_of_returning_a_short_axis()
    {
        await using var db = TestHarness.NewContext(nameof(A_calendar_with_no_working_days_left_refuses_instead_of_returning_a_short_axis));
        db.SeedCatalog();
        db.SeedHoliday(new DateOnly(2025, 9, 1), "Fermeture accidentelle", days: 4_000,
            kind: HolidayKind.Academic);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateAxisWindowsQuery(4, new DateOnly(2025, 10, 1), AxisColumnUnit.WorkingDays, 20),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.AxisDoesNotFit");
    }
}
