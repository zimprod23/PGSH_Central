using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Progression;
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
        new(db, Planner(db), Stamper(db), db.AdminAuthorizer());

    internal static ReinscriptionPlanner Planner(ApplicationDbContext db) =>
        new(db, new OutstandingStageFinder(db));

    /// <summary>
    /// The rollover stamps the governing CNPN onto each registration it creates — it is the act an
    /// effectivity rule authored over the summer actually bites on.
    /// </summary>
    private static RegistrationCnpnStamper Stamper(ApplicationDbContext db) =>
        new(db, new CnpnAssignment(db));

    private static PreviewReinscriptionQueryHandler PreviewHandler(ApplicationDbContext db) =>
        new(Planner(db), db.AdminAuthorizer());

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
            db, Planner(db), Stamper(db), db.StrangerAuthorizer());

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

    [Fact]
    public async Task One_run_rolls_every_promotion_of_the_year_each_from_its_own_level()
    {
        await using var db = TestHarness.NewContext(nameof(One_run_rolls_every_promotion_of_the_year_each_from_its_own_level));
        SeedTwoLevelsAndTwoYears(db);
        db.SeedLevel(levelId: 5, "5ème année", year: 5);

        var third = SeedClosed(db, "Sara", "Bennani", RegistrationStatus.Validated);
        var fourth = db.SeedRegistration("Ali", "Amrani", levelId: NextLevelId);
        fourth.RecordYearOutcome(
            RegistrationStatus.Failed, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        // Level omitted: every promotion of the closing year, each student moving up from his own
        // level. A year is closed in one sitting, so rolling it level by level only invites half of
        // it to be forgotten.
        var result = await ApplyHandler(db).Handle(
            new ApplyReinscriptionCommand(TestHarness.CurrentYearId, NextYearId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.ScopeLabel.Should().Be("Toutes les promotions");
        result.Value.WillRegister.Should().Be(2);
        result.Value.ByLevel.Should().HaveCount(2);

        var created = await db.Registrations.Where(r => r.AcademicYearId == NextYearId).ToListAsync();
        created.Single(r => r.StudentId == third.StudentId).LevelId.Should().Be(NextLevelId);
        created.Single(r => r.StudentId == fourth.StudentId).LevelId.Should().Be(NextLevelId);
    }

    [Fact]
    public async Task Rows_needing_attention_are_never_the_ones_the_cap_hides()
    {
        await using var db = TestHarness.NewContext(nameof(Rows_needing_attention_are_never_the_ones_the_cap_hides));
        SeedTwoLevelsAndTwoYears(db);

        for (int i = 0; i < 20; i++)
            SeedClosed(db, $"Etudiant{i:D2}", "Admis", RegistrationStatus.Validated);

        // The one row an operator has to act on, seeded last so it would sort late in every ordering
        // but the intended one.
        db.SeedRegistration("Zzz", "Sansdecision");
        await db.SaveChangesAsync();

        var report = await PreviewHandler(db).Handle(
            new PreviewReinscriptionQuery(TestHarness.CurrentYearId, NextYearId, TestHarness.LevelId),
            default);

        report.Value.NeedsAttention.Should().Be(1);
        report.Value.Rows[0].Action.Should().Be(ReinscriptionAction.NoOutcome);
    }
}
