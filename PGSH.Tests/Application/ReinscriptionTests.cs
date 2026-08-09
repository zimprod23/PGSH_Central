using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Students.Registrations.Reinscription;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The September half of the rollover: the closed verdicts of one promotion become the next year's
/// registrations. Admis goes up a level, redoublant repeats the same one, and the three outcomes that
/// end a cursus produce nothing at all.
/// </summary>
public class ReinscriptionTests
{
    private const int NextYearId = 3;
    private const int NextLevelId = 4;

    private static ApplyReinscriptionCommandHandler ApplyHandler(ApplicationDbContext db) =>
        new(db, new ReinscriptionPlanner(db), db.AdminAuthorizer());

    private static PreviewReinscriptionQueryHandler PreviewHandler(ApplicationDbContext db) =>
        new(new ReinscriptionPlanner(db), db.AdminAuthorizer());

    /// <summary>
    /// The 3rd year of Médecine closed, the 4th year existing to receive it, and the year after this
    /// one to receive them both.
    /// </summary>
    private static void SeedTwoLevelsAndTwoYears(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedLevel(NextLevelId, "4ème année", year: 4);
        db.SeedAcademicYear(NextYearId, "2026-2027",
            new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));
    }

    private static Registration SeedClosed(
        ApplicationDbContext db, string first, string last, RegistrationStatus outcome,
        RegistrationOutcomeSource source = RegistrationOutcomeSource.Declared)
    {
        var registration = db.SeedRegistration(first, last);
        registration.RecordYearOutcome(outcome, source, null, DateTime.UtcNow);
        return registration;
    }

    [Fact]
    public async Task Admis_moves_up_a_level_and_redoublant_repeats_the_same_one()
    {
        await using var db = TestHarness.NewContext(nameof(Admis_moves_up_a_level_and_redoublant_repeats_the_same_one));
        SeedTwoLevelsAndTwoYears(db);

        var admis = SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated);
        var redoublant = SeedClosed(db, "Ali", "Amrani", RegistrationStatus.Failed);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.WillRegister.Should().Be(2);

        var created = await db.Registrations
            .Where(r => r.AcademicYearId == NextYearId)
            .ToListAsync();

        created.Should().HaveCount(2);
        created.Single(r => r.StudentId == admis.StudentId).LevelId.Should().Be(NextLevelId);
        created.Single(r => r.StudentId == redoublant.StudentId).LevelId.Should().Be(TestHarness.LevelId);

        // Active, not Pending: nothing filters planning by this field, so a Pending row would be
        // grouped and planned exactly like an active one while claiming not to be enrolled.
        created.Should().AllSatisfy(r =>
        {
            r.Status.Should().Be(RegistrationStatus.Active);
            r.OutcomeSource.Should().BeNull();
            // Grouping is auto-arrange's job and runs after this — these land in "Non réparti".
            r.AcademicGroupId.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(RegistrationStatus.Graduated)]
    [InlineData(RegistrationStatus.Excluded)]
    [InlineData(RegistrationStatus.Withdrawn)]
    public async Task An_outcome_that_ends_the_cursus_produces_no_registration(RegistrationStatus outcome)
    {
        await using var db = TestHarness.NewContext($"cursus-ends-{outcome}");
        SeedTwoLevelsAndTwoYears(db);
        SeedClosed(db, "Sara", "Bennani", outcome);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId),
            default);

        result.Value.WillRegister.Should().Be(0);
        result.Value.Rows.Single().Action.Should().Be(ReinscriptionAction.CursusEnded);
        (await db.Registrations.CountAsync(r => r.AcademicYearId == NextYearId)).Should().Be(0);
    }

    [Fact]
    public async Task A_student_whose_year_was_never_closed_is_reported_and_not_carried_over()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_whose_year_was_never_closed_is_reported_and_not_carried_over));
        SeedTwoLevelsAndTwoYears(db);
        db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId),
            default);

        result.Value.WillRegister.Should().Be(0);
        result.Value.NeedsAttention.Should().Be(1);
        result.Value.Rows.Single().Action.Should().Be(ReinscriptionAction.NoOutcome);
    }

    [Fact]
    public async Task Admis_with_no_level_above_is_reported_rather_than_guessed_as_a_graduation()
    {
        await using var db = TestHarness.NewContext(nameof(Admis_with_no_level_above_is_reported_rather_than_guessed_as_a_graduation));
        db.SeedCatalog();
        db.SeedAcademicYear(NextYearId, "2026-2027",
            new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));

        // No 4th year exists — almost always a PV that should have read « Diplômé ».
        SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId),
            default);

        result.Value.WillRegister.Should().Be(0);
        result.Value.Rows.Single().Action.Should().Be(ReinscriptionAction.NextLevelMissing);
        result.Value.NeedsAttention.Should().Be(1);
    }

    [Fact]
    public async Task Running_it_twice_creates_nothing_the_second_time()
    {
        await using var db = TestHarness.NewContext(nameof(Running_it_twice_creates_nothing_the_second_time));
        SeedTwoLevelsAndTwoYears(db);
        SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated);
        await db.SaveChangesAsync();

        var command = new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId);

        await ApplyHandler(db).Handle(command, default);
        var second = await ApplyHandler(db).Handle(command, default);

        // Idempotent on purpose: the rollover is re-run after the odd verdicts are corrected, which is
        // why it skips rather than refusing the whole promotion the way the déliberation import does.
        second.Value.WillRegister.Should().Be(0);
        second.Value.Rows.Single().Action.Should().Be(ReinscriptionAction.AlreadyRegistered);
        (await db.Registrations.CountAsync(r => r.AcademicYearId == NextYearId)).Should().Be(1);
    }

    [Fact]
    public async Task A_correction_after_the_rollover_leaves_the_registration_already_created_alone()
    {
        await using var db = TestHarness.NewContext(nameof(A_correction_after_the_rollover_leaves_the_registration_already_created_alone));
        SeedTwoLevelsAndTwoYears(db);
        var registration = SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated);
        await db.SaveChangesAsync();

        await ApplyHandler(db).Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId), default);

        // The jury corrects itself afterwards. The existing registration outranks the new verdict —
        // undoing it is scolarité's decision, not something a re-run should do silently.
        registration.RecordYearOutcome(
            RegistrationStatus.Failed, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var rerun = await ApplyHandler(db).Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId), default);

        rerun.Value.Rows.Single().Action.Should().Be(ReinscriptionAction.AlreadyRegistered);
        var created = await db.Registrations.SingleAsync(r => r.AcademicYearId == NextYearId);
        created.LevelId.Should().Be(NextLevelId);
    }

    [Fact]
    public async Task Rolling_a_year_into_itself_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(Rolling_a_year_into_itself_is_refused));
        SeedTwoLevelsAndTwoYears(db);
        SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated);
        await db.SaveChangesAsync();

        var result = await PreviewHandler(db).Handle(
            new PreviewReinscriptionQuery(TestHarness.CurrentYearId, TestHarness.CurrentYearId, TestHarness.LevelId),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Reinscription.SameYear");
    }

    [Fact]
    public async Task Rolling_backwards_into_an_earlier_year_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(Rolling_backwards_into_an_earlier_year_is_refused));
        SeedTwoLevelsAndTwoYears(db);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated);
        await db.SaveChangesAsync();

        var result = await PreviewHandler(db).Handle(
            new PreviewReinscriptionQuery(TestHarness.CurrentYearId, TestHarness.PreviousYearId, TestHarness.LevelId),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Reinscription.TargetYearNotLater");
    }

    [Fact]
    public async Task The_preview_is_exactly_what_the_apply_does()
    {
        await using var db = TestHarness.NewContext(nameof(The_preview_is_exactly_what_the_apply_does));
        SeedTwoLevelsAndTwoYears(db);
        SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated);
        SeedClosed(db, "Ali", "Amrani", RegistrationStatus.Failed);
        SeedClosed(db, "Yasmine", "Idrissi", RegistrationStatus.Graduated);
        db.SeedRegistration("Omar", "Tazi");
        await db.SaveChangesAsync();

        var preview = await PreviewHandler(db).Handle(
            new PreviewReinscriptionQuery(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId), default);

        (await db.Registrations.CountAsync(r => r.AcademicYearId == NextYearId)).Should().Be(0);

        var applied = await ApplyHandler(db).Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId), default);

        applied.Value.WillRegister.Should().Be(preview.Value.WillRegister);
        applied.Value.Skipped.Should().Be(preview.Value.Skipped);
        applied.Value.ByTargetLevel.Should().BeEquivalentTo(preview.Value.ByTargetLevel);
    }

    [Fact]
    public async Task A_caller_who_is_not_administrative_cannot_reinscribe_a_promotion()
    {
        await using var db = TestHarness.NewContext(nameof(A_caller_who_is_not_administrative_cannot_reinscribe_a_promotion));
        SeedTwoLevelsAndTwoYears(db);
        SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated);
        await db.SaveChangesAsync();

        var handler = new ApplyReinscriptionCommandHandler(
            db, new ReinscriptionPlanner(db), db.StrangerAuthorizer());

        var result = await handler.Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Reinscription.NotAllowed");
    }

    [Fact]
    public async Task A_verdict_inferred_by_PGSH_still_drives_the_rollover()
    {
        await using var db = TestHarness.NewContext(nameof(A_verdict_inferred_by_PGSH_still_drives_the_rollover));
        SeedTwoLevelsAndTwoYears(db);
        SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated, RegistrationOutcomeSource.Inferred);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId), default);

        // Phase 14.3 will settle the imported years this way; the rollover must read those too, and the
        // report says which source each row came from so nobody mistakes one for the other.
        result.Value.WillRegister.Should().Be(1);
        result.Value.Rows.Single().OutcomeSource.Should().Be(RegistrationOutcomeSource.Inferred);
    }
}
