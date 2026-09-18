using FluentAssertions;
using PGSH.Application.AcademicYears;
using PGSH.Application.Calendar;
using PGSH.Domain.Calendar;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Recording and correcting the calendar. The act that matters here is the one the workflow is built
/// around: a lunar date is entered in September as an estimate and corrected the day the decree names it.
/// Deleting a holiday already reported the slots laid over it; moving one is the <i>same</i> event for
/// every window whose day count was produced against the old date, and it reported nothing.
/// </summary>
public class HolidayCommandTests
{
    private static readonly DateOnly Estimated = new(2026, 3, 19);
    private static readonly DateOnly Decreed   = new(2026, 3, 20);

    private static UpdateHolidayCommandHandler Handler(ApplicationDbContext db) => new(db);

    private static UpdateHolidayCommand Correct(
        int id, DateOnly start, DateOnly end, string name = "Aïd al-Fitr", bool confirmed = true,
        bool? worked = null) =>
        new(id, start, end, name, HolidayKind.Religious, confirmed, worked);

    /// <summary>A holiday over the estimated date, and one slot whose window is laid across it.</summary>
    private static async Task<(Holiday Holiday, ApplicationDbContext Db)> SeedAsync(string name)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog();

        db.SeedSlot(stage, 1, 1, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var holiday = db.SeedHoliday(Estimated, "Aïd al-Fitr", days: 2,
            kind: HolidayKind.Religious, confirmed: false);

        await db.SaveChangesAsync();
        return (holiday, db);
    }

    [Fact]
    public async Task Moving_a_holiday_reports_the_slots_whose_count_no_longer_reproduces()
    {
        var (holiday, db) = await SeedAsync(nameof(Moving_a_holiday_reports_the_slots_whose_count_no_longer_reproduces));
        await using var _ = db;

        var result = await Handler(db).Handle(
            Correct(holiday.Id, Decreed, Decreed.AddDays(1)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.DatesMoved.Should().BeTrue();
        result.Value.SlotsSpanning.Should().Be(1);
        result.Value.StartDate.Should().Be(Decreed);
    }

    [Fact]
    public async Task Confirming_a_date_that_did_not_move_reports_nothing()
    {
        // The common case, and the reason DatesMoved exists: ticking « Date confirmée » on a span that was
        // already right changes no window's day count. Reporting slots here would train the user to
        // dismiss the one report that matters.
        var (holiday, db) = await SeedAsync(nameof(Confirming_a_date_that_did_not_move_reports_nothing));
        await using var _ = db;

        var result = await Handler(db).Handle(
            Correct(holiday.Id, Estimated, Estimated.AddDays(1)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.DatesMoved.Should().BeFalse();
        result.Value.SlotsSpanning.Should().Be(0);
    }

    [Fact]
    public async Task A_slot_over_only_the_new_date_is_reported_too()
    {
        // Both halves of the move are affected and for opposite reasons: a window laid around the old date
        // was built on a holiday that is no longer there, and one covering the new date has just gained a
        // non-working stretch it never counted. Only the second exists here.
        await using var db = TestHarness.NewContext(nameof(A_slot_over_only_the_new_date_is_reported_too));
        var stage = db.SeedCatalog();

        db.SeedSlot(stage, 1, 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var holiday = db.SeedHoliday(Estimated, "Aïd al-Fitr", days: 2, kind: HolidayKind.Religious);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            Correct(holiday.Id, new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 11)), default);

        result.Value.SlotsSpanning.Should().Be(1);
    }

    [Fact]
    public async Task A_slot_spanning_both_the_old_and_the_new_date_is_counted_once()
    {
        // The usual correction is a day either way, so the two spans almost always sit inside one window.
        // Counting it twice would name a number the confirmation cannot justify.
        var (holiday, db) = await SeedAsync(nameof(A_slot_spanning_both_the_old_and_the_new_date_is_counted_once));
        await using var _ = db;

        var result = await Handler(db).Handle(
            Correct(holiday.Id, Decreed, Decreed.AddDays(1)), default);

        result.Value.SlotsSpanning.Should().Be(1);
    }

    [Fact]
    public async Task A_correction_onto_another_holidays_date_and_name_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(A_correction_onto_another_holidays_date_and_name_is_refused));
        db.SeedCatalog();

        var first  = db.SeedHoliday(Estimated, "Aïd al-Fitr", days: 2, kind: HolidayKind.Religious);
        var second = db.SeedHoliday(Decreed, "Aïd al-Fitr", days: 2, kind: HolidayKind.Religious);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            Correct(first.Id, second.StartDate, second.EndDate), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(HolidayErrors.Duplicate(second.StartDate, "Aïd al-Fitr").Code);
    }

    [Fact]
    public async Task An_unknown_holiday_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(An_unknown_holiday_is_refused));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Correct(404, Decreed, Decreed), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(HolidayErrors.NotFound(404).Code);
    }

    [Fact]
    public async Task Declaring_a_holiday_worked_reports_the_slots_it_gives_days_back_to()
    {
        // The second way to change what every window over a date is worth, and it moves no date. Gating
        // the report on DatesMoved alone would have made the one change that gives days *back* the only
        // silent one.
        var (holiday, db) = await SeedAsync(nameof(Declaring_a_holiday_worked_reports_the_slots_it_gives_days_back_to));
        await using var _ = db;

        var result = await Handler(db).Handle(
            Correct(holiday.Id, Estimated, Estimated.AddDays(1), confirmed: false, worked: true), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.DatesMoved.Should().BeFalse("the span is exactly where it was");
        result.Value.CountingChanged.Should().BeTrue();
        result.Value.SlotsSpanning.Should().Be(1);

        db.Holidays.Single(h => h.Id == holiday.Id).CountsAsWorkingDay.Should().BeTrue();
    }

    [Fact]
    public async Task Saving_a_holiday_without_touching_the_flag_reports_nothing()
    {
        // The control for the case above: it must not fire on every save, or it is noise and the real one
        // goes unread.
        var (holiday, db) = await SeedAsync(nameof(Saving_a_holiday_without_touching_the_flag_reports_nothing));
        await using var _ = db;

        var result = await Handler(db).Handle(
            Correct(holiday.Id, Estimated, Estimated.AddDays(1), confirmed: true), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.DatesMoved.Should().BeFalse();
        result.Value.CountingChanged.Should().BeFalse();
        result.Value.SlotsSpanning.Should().Be(0);
    }

    [Fact]
    public async Task A_holiday_the_faculty_works_through_costs_no_working_day()
    {
        // ⚠ Zero has two opposite meanings on this screen, which is why the flag travels beside the
        // number: a holiday costing nothing because it fell on a Sunday is a row to leave alone, one
        // costing nothing because it is worked is a row somebody flagged and may want to unflag.
        await using var db = TestHarness.NewContext(nameof(A_holiday_the_faculty_works_through_costs_no_working_day));
        db.SeedCatalog();

        // Thursday 1 January 2026 and Monday 11 May 2026 — both ordinary worked days but for the rows.
        db.SeedHoliday(new DateOnly(2026, 1, 1), "Nouvel An", countsAsWorkingDay: true);
        db.SeedHoliday(new DateOnly(2026, 5, 11), "Journée pédagogique", kind: HolidayKind.Academic);
        await db.SaveChangesAsync();

        var result = await new GetHolidayCoverageQueryHandler(db, new AcademicYearResolver(db))
            .Handle(new GetHolidayCoverageQuery(), default);

        result.IsSuccess.Should().BeTrue();

        var worked = result.Value.Holidays.Single(h => h.Name == "Nouvel An");
        worked.CountsAsWorkingDay.Should().BeTrue();
        worked.WorkingDaysLost.Should().Be(0, "nobody loses a day the faculty works through");

        var closed = result.Value.Holidays.Single(h => h.Name == "Journée pédagogique");
        closed.CountsAsWorkingDay.Should().BeFalse();
        closed.WorkingDaysLost.Should().Be(1, "the control: an ordinary Monday closure costs its day");

        result.Value.WorkedThroughCount.Should().Be(1);
    }

    [Fact]
    public async Task A_save_that_omits_the_flag_leaves_it_where_it_was()
    {
        // The hazard the nullable exists for: this is a full-replace PUT and the screen lives in another
        // repository. A client that has never heard of « travaillé » saves a name and must not undo a
        // flag somebody set on purpose.
        var (holiday, db) = await SeedAsync(nameof(A_save_that_omits_the_flag_leaves_it_where_it_was));
        holiday.CountsAsWorkingDay = true;
        await db.SaveChangesAsync();
        await using var _ = db;

        var result = await Handler(db).Handle(
            Correct(holiday.Id, Estimated, Estimated.AddDays(1), name: "Aïd al-Fitr (corrigé)"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.CountingChanged.Should().BeFalse();
        db.Holidays.Single(h => h.Id == holiday.Id).CountsAsWorkingDay
            .Should().BeTrue("an omitted field is not a request for « chômé »");

        // …and the control: asking for it explicitly does change it.
        var undone = await Handler(db).Handle(
            Correct(holiday.Id, Estimated, Estimated.AddDays(1), name: "Aïd al-Fitr (corrigé)",
                worked: false), default);

        undone.Value.CountingChanged.Should().BeTrue();
        db.Holidays.Single(h => h.Id == holiday.Id).CountsAsWorkingDay.Should().BeFalse();
    }
}
