using FluentAssertions;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Stages.InternshipAssignments.GetDossier;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The level dossier folds every registration a student holds at one level. A repeating student carries
// a fresh registration per year and fresh assignments under it, so "what does this student still owe at
// this level?" cannot be answered from a single assignment — which is exactly what decides what gets
// planned next year and what has to be re-opened in revalidation.
public class StudentLevelDossierTests
{
    private const int CardiologieId = TestHarness.StageId;   // 1
    private const int PediatrieId   = 2;
    private const int ServiceId     = 1;

    private sealed record Scenario(Registration Previous, Registration Current, Cohort PreviousCohort, Cohort CurrentCohort);

    // Two years at the same level: 2024-2025 (failed) then 2025-2026 (the retake).
    private static Scenario SeedTwoYears(ApplicationDbContext db)
    {
        var cardio = db.SeedCatalog();
        db.SeedStage(PediatrieId, "Pédiatrie", coefficient: 3);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        db.SeedService(ServiceId, "Cardiologie");

        var previousCohort = db.SeedCohort(cardio, 20, "Groupe 20", TestHarness.PreviousYearId);
        var currentCohort  = db.SeedCohort(cardio, 10, "Groupe 10");

        var previous = db.SeedRegistration("Omar", "Tazi", previousCohort.AcademicGroup,
            academicYearId: TestHarness.PreviousYearId);
        var current = db.SeedRegistration("Omar", "Tazi", currentCohort.AcademicGroup);

        // Same human being across both years — SeedRegistration mints a student per call.
        current.StudentId = previous.StudentId;
        current.Student   = previous.Student;

        return new Scenario(previous, current, previousCohort, currentCohort);
    }

    private static GetStudentLevelDossierQueryHandler Handler(ApplicationDbContext db, ExecutionAuthorizer? authorizer = null) =>
        new(db, authorizer ?? db.AdminAuthorizer());

    private static DossierStage StageOf(StudentLevelDossierResponse dossier, int stageId) =>
        dossier.Stages.Single(s => s.StageId == stageId);

    [Fact]
    public async Task Attempts_from_every_registration_at_the_level_are_folded_together()
    {
        await using var db = TestHarness.NewContext("dossier-fold");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 6m,  from: new DateOnly(2024, 10, 1));
        db.SeedGradedAssignment(s.Current,  s.CurrentCohort,  service, mark: 14m, from: new DateOnly(2025, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(s.Previous.StudentId, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Registrations.Should().HaveCount(2);

        var cardio = StageOf(result.Value, CardiologieId);
        cardio.AttemptCount.Should().Be(2);
        cardio.State.Should().Be(DossierStageState.Validated);
        cardio.BestScore.Should().Be(14m);
        // Most recent year first.
        cardio.Attempts[0].AcademicYearLabel.Should().Be("2025-2026");
        cardio.Attempts[1].AcademicYearLabel.Should().Be("2024-2025");
    }

    [Fact]
    public async Task A_stage_failed_on_every_attempt_is_flagged_for_revalidation()
    {
        await using var db = TestHarness.NewContext("dossier-torevalidate");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 7m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(s.Previous.StudentId, TestHarness.LevelId), default);

        StageOf(result.Value, CardiologieId).State.Should().Be(DossierStageState.ToRevalidate);
    }

    [Fact]
    public async Task A_stage_passed_in_an_earlier_year_stays_acquired_and_is_never_owed_again()
    {
        await using var db = TestHarness.NewContext("dossier-acquired");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        // Passed in 2024-2025; the student repeats the year for other subjects and never serves it again.
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 12m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(s.Previous.StudentId, TestHarness.LevelId), default);

        var cardio = StageOf(result.Value, CardiologieId);
        cardio.State.Should().Be(DossierStageState.Validated);
        cardio.Attempts.Should().ContainSingle();
        result.Value.StagesValidated.Should().Be(1);
    }

    [Fact]
    public async Task A_stage_still_awaiting_its_verdict_is_in_progress_not_a_retake()
    {
        await using var db = TestHarness.NewContext("dossier-inprogress");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        var assignment = db.SeedAssignment(s.Current, s.CurrentCohort);
        db.SeedPeriod(assignment, service, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(s.Current.StudentId, TestHarness.LevelId), default);

        var cardio = StageOf(result.Value, CardiologieId);
        cardio.State.Should().Be(DossierStageState.InProgress);
        cardio.BestScore.Should().BeNull();
    }

    [Fact]
    public async Task A_stage_of_the_level_never_attempted_is_reported_as_such()
    {
        await using var db = TestHarness.NewContext("dossier-notattempted");
        var s = SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(s.Current.StudentId, TestHarness.LevelId), default);

        result.Value.Stages.Should().HaveCount(2);
        StageOf(result.Value, PediatrieId).State.Should().Be(DossierStageState.NotAttempted);
        StageOf(result.Value, PediatrieId).AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task The_level_is_complete_only_when_every_stage_is_validated()
    {
        await using var db = TestHarness.NewContext("dossier-complete");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        var pediatrie = db.Stages.Local.First(x => x.Id == PediatrieId);
        var pediatrieCohort = db.SeedCohort(pediatrie, 11, "Groupe 10 — Pédiatrie");

        db.SeedGradedAssignment(s.Current, s.CurrentCohort, service, mark: 15m, from: new DateOnly(2025, 10, 1));
        await db.SaveChangesAsync();

        var partial = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(s.Current.StudentId, TestHarness.LevelId), default);

        partial.Value.StagesTotal.Should().Be(2);
        partial.Value.StagesValidated.Should().Be(1);
        partial.Value.StagesOutstanding.Should().Be(1);
        partial.Value.IsLevelComplete.Should().BeFalse();

        db.SeedGradedAssignment(s.Current, pediatrieCohort, service, mark: 11m, from: new DateOnly(2025, 12, 1));
        await db.SaveChangesAsync();

        var full = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(s.Current.StudentId, TestHarness.LevelId), default);

        full.Value.StagesValidated.Should().Be(2);
        full.Value.StagesOutstanding.Should().Be(0);
        full.Value.IsLevelComplete.Should().BeTrue();
    }

    // Regression: attempts used to be found through the registrations *of this level*, so a retake of
    // an earlier level's stage — which hangs off the registration the student holds now — was invisible
    // in both dossiers at once, and the earlier level stayed ToRevalidate forever even once it passed.
    [Fact]
    public async Task A_retake_served_under_a_later_levels_registration_still_counts_for_the_earlier_level()
    {
        await using var db = TestHarness.NewContext("dossier-cross-level");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        // Failed the level-1 stage in 2024-2025.
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 6m, from: new DateOnly(2024, 10, 1));

        // Now registered at a DIFFERENT level, and the retake of the level-1 stage hangs off it.
        var sixthLevel = new Level { Id = 9, Label = "6ème année", Year = 6 };
        db.Levels.Add(sixthLevel);
        var sixthGroup = new AcademicGroup
        {
            Id = 60, Label = "Groupe 60", GroupNumber = 60, AcademicYearId = TestHarness.CurrentYearId,
        };
        db.AcademicGroups.Add(sixthGroup);
        var sixthReg = db.SeedRegistration("Omar", "Tazi", sixthGroup, levelId: 9);
        sixthReg.StudentId = s.Previous.StudentId;
        sixthReg.Student   = s.Previous.Student;

        // The retake still points at the level-1 stage through its cohort.
        db.SeedGradedAssignment(sixthReg, s.CurrentCohort, service, mark: 14m, from: new DateOnly(2026, 2, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(s.Previous.StudentId, TestHarness.LevelId), default);

        var cardio = StageOf(result.Value, CardiologieId);
        cardio.AttemptCount.Should().Be(2, "the retake belongs to this level's stage, whatever registration carries it");
        cardio.State.Should().Be(DossierStageState.Validated);
        cardio.BestScore.Should().Be(14m);
    }

    [Fact]
    public async Task An_unknown_student_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("dossier-missing-student");
        SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(Guid.NewGuid(), TestHarness.LevelId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Students.NotFound");
    }

    [Fact]
    public async Task An_unknown_level_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("dossier-missing-level");
        var s = SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetStudentLevelDossierQuery(s.Current.StudentId, LevelId: 999), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotFound");
    }

    [Fact]
    public async Task A_caller_who_is_neither_the_administration_nor_the_student_is_refused()
    {
        await using var db = TestHarness.NewContext("dossier-stranger");
        var s = SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db, db.StrangerAuthorizer()).Handle(
            new GetStudentLevelDossierQuery(s.Current.StudentId, TestHarness.LevelId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.DossierReadNotAllowed);
    }

    [Fact]
    public async Task A_student_may_read_their_own_dossier()
    {
        await using var db = TestHarness.NewContext("dossier-self");
        var s = SeedTwoYears(db);

        // The dossier is keyed on the local Student PK, which the authorizer resolves from the
        // Keycloak subject — so the caller must be linked to that very student row.
        var keycloakId = Guid.NewGuid();
        s.Previous.Student.LinkIdentity(keycloakId.ToString());
        await db.SaveChangesAsync();

        var selfAuthorizer = new ExecutionAuthorizer(db, TestHarness.UserContext(keycloakId, Roles.Student));

        var result = await Handler(db, selfAuthorizer).Handle(
            new GetStudentLevelDossierQuery(s.Current.StudentId, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.StudentId.Should().Be(s.Current.StudentId);
    }
}
