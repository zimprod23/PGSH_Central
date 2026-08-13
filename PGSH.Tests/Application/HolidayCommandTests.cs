using FluentAssertions;
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
        int id, DateOnly start, DateOnly end, string name = "Aïd al-Fitr", bool confirmed = true) =>
        new(id, start, end, name, HolidayKind.Religious, confirmed);

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
}
