using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears.Manage;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Setting and removing an academic year. Both acts are guarded by things a year cannot see about
/// itself — the other years' calendars, and everything the year constitutes.
/// </summary>
/// <remarks>
/// ⚠ <c>UseInMemoryDatabase</c> ignores foreign keys, so the delete guards here prove the
/// <em>refusal</em>, never the cascade behind it. The cascade is what the guards exist to keep the
/// user away from, and it is read off the live schema in <c>SMOKE-TEST.md</c> §23.
/// </remarks>
public class AcademicYearManagementTests
{
    private const int Current = TestHarness.CurrentYearId;
    private const int Future = 900;

    private static ApplicationDbContext Seed(string name)
    {
        var db = TestHarness.NewContext(name);
        db.SeedCatalog();

        db.AcademicYears.Add(new AcademicYear
        {
            Id = Future, Label = "2026-2027", IsCurrent = false,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 8, 31),
        });

        db.SaveChanges();
        return db;
    }

    private static SetCurrentAcademicYearCommandHandler SetCurrent(ApplicationDbContext db) =>
        new(db, new CurrentYearDesignation(db), db.AdminAuthorizer());

    private static DeleteAcademicYearCommandHandler Delete(ApplicationDbContext db) =>
        new(db, db.AdminAuthorizer());

    private static UpdateAcademicYearCommandHandler Update(ApplicationDbContext db) =>
        new(db, new AcademicYearCalendarGuard(db), db.AdminAuthorizer());

    // ─── Designating the current year ─────────────────────────────────────────

    [Fact]
    public async Task Designating_a_year_stands_the_previous_one_down()
    {
        await using var db = Seed("year-set-current");

        var result = await SetCurrent(db).Handle(new SetCurrentAcademicYearCommand(Future), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PreviousLabel.Should().Be("2025-2026", "the confirmation has to say what changed");

        var years = await db.AcademicYears.AsNoTracking().ToListAsync();
        years.Should().ContainSingle(y => y.IsCurrent)
            .Which.Id.Should().Be(Future);
    }

    /// <summary>
    /// ⚠ The invariant the whole feature turns on. <c>IX_AcademicYear_IsCurrent</c> is unique and
    /// filtered, so two flagged rows is a constraint violation — but in-memory ignores the index, which
    /// is precisely why the count is asserted here rather than left to the database to catch.
    /// </summary>
    [Fact]
    public async Task Exactly_one_year_is_current_however_many_times_it_moves()
    {
        await using var db = Seed("year-set-current-twice");

        await SetCurrent(db).Handle(new SetCurrentAcademicYearCommand(Future), default);
        await SetCurrent(db).Handle(new SetCurrentAcademicYearCommand(Current), default);

        (await db.AcademicYears.AsNoTracking().CountAsync(y => y.IsCurrent)).Should().Be(1);
    }

    [Fact]
    public async Task Designating_the_year_that_already_holds_it_is_refused()
    {
        await using var db = Seed("year-already-current");

        var result = await SetCurrent(db).Handle(new SetCurrentAcademicYearCommand(Current), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.AlreadyCurrent");
        (await db.AcademicYears.AsNoTracking().CountAsync(y => y.IsCurrent)).Should().Be(1);
    }

    [Fact]
    public async Task Designating_a_year_that_does_not_exist_leaves_the_current_one_alone()
    {
        await using var db = Seed("year-set-current-missing");

        var result = await SetCurrent(db).Handle(new SetCurrentAcademicYearCommand(4242), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.NotFound");

        // ⚠ The assertion that matters: the demote must not have run. A guard ordered after it would
        // leave the base with no current year at all, and every unscoped handler failing.
        (await db.AcademicYears.AsNoTracking().CountAsync(y => y.IsCurrent)).Should().Be(1);
    }

    // ─── Deleting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_empty_year_is_deleted()
    {
        await using var db = Seed("year-delete-empty");

        var result = await Delete(db).Handle(new DeleteAcademicYearCommand(Future), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.RostersRemoved.Should().Be(0);
        (await db.AcademicYears.AsNoTracking().AnyAsync(y => y.Id == Future)).Should().BeFalse();
    }

    /// <summary>
    /// The application resolves every unscoped read through the current year. Removing it leaves no
    /// answer to « quelle année ? », and designating another one first is the reversible act.
    /// </summary>
    [Fact]
    public async Task The_current_year_is_never_deleted()
    {
        await using var db = Seed("year-delete-current");

        var result = await Delete(db).Handle(new DeleteAcademicYearCommand(Current), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.CannotDeleteCurrent");
        (await db.AcademicYears.AsNoTracking().AnyAsync(y => y.Id == Current)).Should().BeTrue();
    }

    [Fact]
    public async Task A_year_holding_registrations_is_refused_and_the_refusal_names_them()
    {
        await using var db = Seed("year-delete-registrations");
        db.SeedRegistration("Nadia", "Alaoui", academicYearId: Future);
        await db.SaveChangesAsync();

        var result = await Delete(db).Handle(new DeleteAcademicYearCommand(Future), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.StillInUse");
        result.Error.Description.Should().Contain("1 inscription(s)");
        (await db.AcademicYears.AsNoTracking().AnyAsync(y => y.Id == Future)).Should().BeTrue();
    }

    /// <summary>
    /// Every reason at once, because a user who clears the registrations only to be told about the
    /// périodes has been sent round the loop twice for nothing.
    /// </summary>
    [Fact]
    public async Task The_refusal_lists_every_holding_not_just_the_first()
    {
        await using var db = Seed("year-delete-many-holdings");
        db.SeedRegistration("Karim", "Bennis", academicYearId: Future);
        db.StageSlots.Add(new PGSH.Domain.Stages.StageSlot
        {
            Id = 9001, StageId = TestHarness.StageId, AcademicYearId = Future, PeriodNumber = 1,
            StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 11, 30),
        });
        await db.SaveChangesAsync();

        var result = await Delete(db).Handle(new DeleteAcademicYearCommand(Future), default);

        result.Error.Description.Should().Contain("1 inscription(s)").And.Contain("1 période(s) de stage");
    }

    // ─── Calendar ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Not a tidiness rule: <c>ServiceOccupancyCalculator</c> bounds a year by its dates rather than
    /// by its id, so a day belonging to two years counts every slot in the overlap twice.
    /// </summary>
    [Fact]
    public async Task A_year_cannot_be_moved_onto_another_years_days()
    {
        await using var db = Seed("year-overlap");

        var result = await Update(db).Handle(
            new UpdateAcademicYearCommand(
                Future, "2026-2027", new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.OverlapsAnotherYear");
    }

    /// <summary>The control: the same edit, on days nobody else claims, must go through.</summary>
    [Fact]
    public async Task A_year_moved_onto_free_days_is_saved()
    {
        await using var db = Seed("year-move-ok");

        var result = await Update(db).Handle(
            new UpdateAcademicYearCommand(
                Future, "2026-2027 (rectifiée)", new DateOnly(2026, 10, 1), new DateOnly(2027, 9, 30)),
            default);

        result.IsSuccess.Should().BeTrue();
        var stored = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == Future);
        stored.Label.Should().Be("2026-2027 (rectifiée)");
        stored.StartDate.Should().Be(new DateOnly(2026, 10, 1));
    }

    /// <summary>A year must not collide with the row being edited — itself.</summary>
    [Fact]
    public async Task Re_saving_a_year_unchanged_does_not_collide_with_itself()
    {
        await using var db = Seed("year-self-overlap");

        var result = await Update(db).Handle(
            new UpdateAcademicYearCommand(
                Future, "2026-2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)),
            default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_year_cannot_take_a_label_another_year_already_has()
    {
        await using var db = Seed("year-duplicate-label");

        var result = await Update(db).Handle(
            new UpdateAcademicYearCommand(
                Future, "2025-2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.DuplicateLabel");
    }

    [Fact]
    public async Task A_year_cannot_end_before_it_starts()
    {
        await using var db = Seed("year-inverted");

        var result = await Update(db).Handle(
            new UpdateAcademicYearCommand(
                Future, "2026-2027", new DateOnly(2027, 9, 1), new DateOnly(2026, 8, 31)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.EndsBeforeItStarts");
    }

    /// <summary>
    /// Narrowing a year does not move the périodes laid on it, so they can end up outside their own
    /// year. Reported rather than refused — a year is routinely corrected while its axis is a draft —
    /// but reported <em>before</em> the write, or the slots that fell out become indistinguishable
    /// from slots that were always elsewhere.
    /// </summary>
    [Fact]
    public async Task Narrowing_a_year_reports_the_periodes_it_leaves_outside()
    {
        await using var db = Seed("year-narrow");
        db.StageSlots.Add(new PGSH.Domain.Stages.StageSlot
        {
            Id = 9002, StageId = TestHarness.StageId, AcademicYearId = Future, PeriodNumber = 1,
            StartDate = new DateOnly(2027, 7, 1), EndDate = new DateOnly(2027, 7, 31),
        });
        await db.SaveChangesAsync();

        var result = await Update(db).Handle(
            new UpdateAcademicYearCommand(
                Future, "2026-2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30)),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.SlotsOutsideSpan.Should().Be(1);
    }
}
