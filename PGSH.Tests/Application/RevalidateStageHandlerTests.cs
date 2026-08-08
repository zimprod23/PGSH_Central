using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Revalidation;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Re-opening a stage the student failed under an earlier registration. The failed attempt is history
// and stays untouched — a retake is a brand-new assignment on the registration the student holds now,
// which is how the domain already models revalidation.
public class RevalidateStageHandlerTests
{
    private const int ServiceId   = 1;
    private const int OtherLevelId = 9;

    private sealed record Scenario(Registration Previous, Registration Current, Cohort PreviousCohort, Cohort CurrentCohort);

    private static Scenario SeedTwoYears(ApplicationDbContext db, bool currentHasGroup = true)
    {
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        db.SeedService(ServiceId, "Cardiologie");

        var previousCohort = db.SeedCohort(stage, 20, "Groupe 20", TestHarness.PreviousYearId);
        var currentCohort  = db.SeedCohort(stage, 10, "Groupe 10");

        var previous = db.SeedRegistration("Omar", "Tazi", previousCohort.AcademicGroup,
            academicYearId: TestHarness.PreviousYearId);
        var current = db.SeedRegistration("Omar", "Tazi",
            currentHasGroup ? currentCohort.AcademicGroup : null);

        current.StudentId = previous.StudentId;
        current.Student   = previous.Student;

        return new Scenario(previous, current, previousCohort, currentCohort);
    }

    private static RevalidateStageCommandHandler Handler(ApplicationDbContext db) =>
        new(db, db.AdminAuthorizer());

    [Fact]
    public async Task A_stage_failed_last_year_is_re_opened_as_a_fresh_assignment()
    {
        await using var db = TestHarness.NewContext("reval-happy");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        var failed = db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 6m,
            from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Current.Id, TestHarness.StageId, Reason: "Redoublement"), default);

        result.IsSuccess.Should().BeTrue();

        var created = await db.InternshipAssignments.FirstAsync(a => a.Id == result.Value);
        created.RegistrationId.Should().Be(s.Current.Id);
        created.CurrentCohortId.Should().Be(s.CurrentCohort.Id);
        created.Status.Should().Be(InternshipStatus.Planned);
        created.Result.Should().Be(StageAssignmentResult.NonÉvalué);

        // The failure is history: it keeps its own mark and verdict.
        var previous = await db.InternshipAssignments.FirstAsync(a => a.Id == failed.Id);
        previous.FinalScore.Should().Be(6m);
        previous.Result.Should().Be(StageAssignmentResult.NonValidé);
    }

    [Fact]
    public async Task The_retake_records_its_opening_membership()
    {
        await using var db = TestHarness.NewContext("reval-membership");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 5m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Current.Id, TestHarness.StageId), default);

        var created = await db.InternshipAssignments
            .Include(a => a.MembershipHistory)
            .FirstAsync(a => a.Id == result.Value);

        var membership = created.MembershipHistory.Should().ContainSingle().Subject;
        membership.CohortId.Should().Be(s.CurrentCohort.Id);
        membership.EndDate.Should().BeNull();
    }

    // A retake is served where the student failed it, not wherever this year's grid would send their
    // group. Failures scatter — CHIRURGIE has 377 across 26 services on the real data — so the target
    // service is per-student; batching several students is only a convenience over that rule.
    [Fact]
    public async Task The_retake_is_placed_back_in_the_service_where_the_student_failed()
    {
        await using var db = TestHarness.NewContext("reval-same-service");
        var s = SeedTwoYears(db);
        var original = db.Services.Local.First();
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, original, mark: 6m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(
                s.Current.Id, TestHarness.StageId,
                StartDate: new DateOnly(2026, 2, 1),
                EndDate:   new DateOnly(2026, 3, 1)),
            default);

        result.IsSuccess.Should().BeTrue();

        var created = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .FirstAsync(a => a.Id == result.Value);

        var period = created.ServicePeriods.Should().ContainSingle().Subject;
        period.ServiceId.Should().Be(original.Id, "the retake goes back where it was failed");
        // Ad-hoc, outside the published schedule — the same meaning délocalisation relies on.
        period.CohortSlotAssignmentId.Should().BeNull();
    }

    [Fact]
    public async Task An_approved_change_of_service_overrides_the_original()
    {
        await using var db = TestHarness.NewContext("reval-service-change");
        var s = SeedTwoYears(db);
        var original = db.Services.Local.First();
        var elsewhere = db.SeedService(77, "HMIMV — Cardiologie");
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, original, mark: 6m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(
                s.Current.Id, TestHarness.StageId,
                ServiceId: elsewhere.Id,
                StartDate: new DateOnly(2026, 2, 1),
                EndDate:   new DateOnly(2026, 3, 1),
                DemandeId: Guid.NewGuid()),
            default);

        var created = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .FirstAsync(a => a.Id == result.Value);

        created.ServicePeriods.Single().ServiceId.Should().Be(elsewhere.Id);
    }

    [Fact]
    public async Task Opening_a_revalidation_without_dates_leaves_it_to_be_scheduled()
    {
        await using var db = TestHarness.NewContext("reval-unplaced");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 6m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Current.Id, TestHarness.StageId), default);

        var created = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .FirstAsync(a => a.Id == result.Value);

        created.ServicePeriods.Should().BeEmpty();
        created.Status.Should().Be(InternshipStatus.Planned);
    }

    [Fact]
    public async Task Half_a_placement_window_is_refused_rather_than_guessed_at()
    {
        await using var db = TestHarness.NewContext("reval-half-window");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 6m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(
                s.Current.Id, TestHarness.StageId, StartDate: new DateOnly(2026, 2, 1)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.IncompletePlacement);
    }

    // NOT covered: StageErrors.OriginalServiceUnknown — a failed attempt carrying no rotation. It is
    // unreachable through the domain, because NonValidé requires an evaluation and an evaluation
    // requires a closed period, so a failed assignment always has at least one service. The guard is
    // kept as a defence against corrupt data; faking that state here would only test the fake.

    [Fact]
    public async Task Only_the_administration_may_open_a_revalidation()
    {
        await using var db = TestHarness.NewContext("reval-forbidden");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 6m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var handler = new RevalidateStageCommandHandler(db, db.StrangerAuthorizer());
        var result = await handler.Handle(new RevalidateStageCommand(s.Current.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.RevalidationNotAllowed);
    }

    [Fact]
    public async Task An_unknown_registration_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("reval-missing-reg");
        SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(Guid.NewGuid(), TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.NotFound");
    }

    [Fact]
    public async Task An_unknown_stage_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("reval-missing-stage");
        var s = SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new RevalidateStageCommand(s.Current.Id, StageId: 999), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NotFound(999));
    }

    // A stage is not necessarily a criterion for failing the year, so an unvalidated one is carried
    // forward: a student may still be redoing a 1st-year stage in their 6th year. The retake is served
    // in a cohort currently running that stage, which the caller names because no cohort of it hangs
    // off any group the student still belongs to.
    private sealed record CrossLevel(Registration Sixth, Cohort HostCohort, Guid FailedAssignmentId);

    private static CrossLevel SeedSixthYearOwingFirstYearStage(ApplicationDbContext db)
    {
        var firstYearStage = db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2020-2021",
            new DateOnly(2020, 9, 1), new DateOnly(2021, 8, 31));
        var service = db.SeedService(ServiceId, "Cardiologie");

        db.Levels.Add(new Level { Id = OtherLevelId, Label = "6ème année", Year = 6 });

        var oldCohort = db.SeedCohort(firstYearStage, 20, "Groupe 20", TestHarness.PreviousYearId);
        var first = db.SeedRegistration("Omar", "Tazi", oldCohort.AcademicGroup,
            academicYearId: TestHarness.PreviousYearId);

        // This year's 1st-year cohort still runs the stage — that is where the retake is served.
        var hostCohort = db.SeedCohort(firstYearStage, 10, "Groupe 10 — 1ère année");

        // The student now sits in a 6th-year group, which has no cohort for a 1st-year stage.
        var sixthGroup = new AcademicGroup
        {
            Id = 60, Label = "Groupe 60", GroupNumber = 60, AcademicYearId = TestHarness.CurrentYearId,
        };
        db.AcademicGroups.Add(sixthGroup);
        var sixth = db.SeedRegistration("Omar", "Tazi", sixthGroup, levelId: OtherLevelId);
        sixth.StudentId = first.StudentId;
        sixth.Student   = first.Student;

        var failed = db.SeedGradedAssignment(first, oldCohort, service, mark: 8m, from: new DateOnly(2020, 10, 1));
        return new CrossLevel(sixth, hostCohort, failed.Id);
    }

    [Fact]
    public async Task A_first_year_stage_can_be_revalidated_in_the_sixth_year_by_naming_the_host_cohort()
    {
        await using var db = TestHarness.NewContext("reval-cross-level");
        var s = SeedSixthYearOwingFirstYearStage(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Sixth.Id, TestHarness.StageId, CohortId: s.HostCohort.Id,
                Reason: "Rattrapage stage 1ère année"), default);

        result.IsSuccess.Should().BeTrue();

        var created = await db.InternshipAssignments.FirstAsync(a => a.Id == result.Value);
        created.RegistrationId.Should().Be(s.Sixth.Id);
        created.CurrentCohortId.Should().Be(s.HostCohort.Id);
        created.Status.Should().Be(InternshipStatus.Planned);
    }

    [Fact]
    public async Task Without_a_named_cohort_the_cross_level_retake_says_which_field_is_missing()
    {
        await using var db = TestHarness.NewContext("reval-cross-level-nocohort");
        var s = SeedSixthYearOwingFirstYearStage(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Sixth.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NoCohortForRevalidation(TestHarness.StageId));
    }

    [Fact]
    public async Task An_unknown_named_cohort_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("reval-cohort-missing");
        var s = SeedSixthYearOwingFirstYearStage(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Sixth.Id, TestHarness.StageId, CohortId: 999), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.CohortNotFound(999));
    }

    [Fact]
    public async Task A_named_cohort_belonging_to_a_different_stage_is_refused()
    {
        await using var db = TestHarness.NewContext("reval-cohort-wrong-stage");
        var s = SeedSixthYearOwingFirstYearStage(db);

        // A cohort of a different stage would silently serve the wrong rotation.
        var otherStage = db.SeedStage(42, "Pédiatrie");
        var otherCohort = db.SeedCohort(otherStage, 30, "Groupe 30 — Pédiatrie");
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Sixth.Id, TestHarness.StageId, CohortId: otherCohort.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.CohortNotForStage(otherCohort.Id, TestHarness.StageId));
    }

    [Fact]
    public async Task A_named_cohort_overrides_the_students_own_group()
    {
        await using var db = TestHarness.NewContext("reval-cohort-wins");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 6m, from: new DateOnly(2024, 10, 1));

        var stage = db.Stages.Local.First(x => x.Id == TestHarness.StageId);
        var elsewhere = db.SeedCohort(stage, 31, "Groupe 31");
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Current.Id, TestHarness.StageId, CohortId: elsewhere.Id), default);

        result.IsSuccess.Should().BeTrue();
        var created = await db.InternshipAssignments.FirstAsync(a => a.Id == result.Value);
        created.CurrentCohortId.Should().Be(elsewhere.Id);
        created.CurrentCohortId.Should().NotBe(s.CurrentCohort.Id);
    }

    [Fact]
    public async Task A_stage_already_carried_by_this_registration_is_not_re_opened()
    {
        await using var db = TestHarness.NewContext("reval-duplicate");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 6m, from: new DateOnly(2024, 10, 1));
        db.SeedAssignment(s.Current, s.CurrentCohort);   // the retake already exists
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Current.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AlreadyAssignedForStage(TestHarness.StageId));
    }

    [Fact]
    public async Task A_stage_never_attempted_belongs_to_ordinary_planning_not_revalidation()
    {
        await using var db = TestHarness.NewContext("reval-nothing");
        var s = SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Current.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NothingToRevalidate(TestHarness.StageId));
    }

    [Fact]
    public async Task A_stage_already_validated_in_an_earlier_year_is_never_repeated()
    {
        await using var db = TestHarness.NewContext("reval-acquired");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 13m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Current.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.StageAlreadyValidated(TestHarness.StageId));
    }

    [Fact]
    public async Task A_stage_still_awaiting_its_verdict_cannot_be_re_opened_underneath()
    {
        await using var db = TestHarness.NewContext("reval-still-open");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        // Served but never evaluated: the verdict may still come back Validé.
        var pending = db.SeedAssignment(s.Previous, s.PreviousCohort);
        db.SeedPeriod(pending, service, new DateOnly(2024, 10, 1), new DateOnly(2024, 10, 31));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Current.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.RevalidationStillOpen(TestHarness.StageId));
    }

    // Regression: the guard used to be Any(NonValidé), so a student holding one settled failure AND
    // one attempt still awaiting its verdict got a retake opened alongside the live one — two
    // attempts running at once, while the dossier called the same student InProgress.
    [Fact]
    public async Task A_settled_failure_alongside_a_pending_attempt_does_not_open_a_retake()
    {
        await using var db = TestHarness.NewContext("reval-mixed-attempts");
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        var service = db.SeedService(ServiceId, "Cardiologie");

        var failedCohort  = db.SeedCohort(stage, 20, "Groupe 20", TestHarness.PreviousYearId);
        var pendingCohort = db.SeedCohort(stage, 21, "Groupe 21", TestHarness.PreviousYearId);
        var currentCohort = db.SeedCohort(stage, 10, "Groupe 10");

        var failedReg = db.SeedRegistration("Omar", "Tazi", failedCohort.AcademicGroup,
            academicYearId: TestHarness.PreviousYearId);
        db.SeedGradedAssignment(failedReg, failedCohort, service, mark: 6m, from: new DateOnly(2024, 10, 1));

        // A second registration whose attempt is served but never graded — it may still pass.
        var pendingReg = db.SeedRegistration("Omar", "Tazi", pendingCohort.AcademicGroup,
            academicYearId: TestHarness.PreviousYearId);
        pendingReg.StudentId = failedReg.StudentId;
        pendingReg.Student   = failedReg.Student;
        var pending = db.SeedAssignment(pendingReg, pendingCohort);
        db.SeedPeriod(pending, service, new DateOnly(2025, 1, 5), new DateOnly(2025, 2, 5));

        var current = db.SeedRegistration("Omar", "Tazi", currentCohort.AcademicGroup);
        current.StudentId = failedReg.StudentId;
        current.Student   = failedReg.Student;
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(current.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.RevalidationStillOpen(TestHarness.StageId));
    }

    [Fact]
    public async Task A_registration_with_no_group_cannot_carry_a_retake()
    {
        await using var db = TestHarness.NewContext("reval-no-group");
        var s = SeedTwoYears(db, currentHasGroup: false);
        var service = db.Services.Local.First();
        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 4m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(s.Current.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NoGroupForRevalidation);
    }

    [Fact]
    public async Task A_group_with_no_cohort_for_the_stage_is_refused()
    {
        await using var db = TestHarness.NewContext("reval-no-cohort");
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        var service = db.SeedService(ServiceId, "Cardiologie");

        var previousCohort = db.SeedCohort(stage, 20, "Groupe 20", TestHarness.PreviousYearId);
        var previous = db.SeedRegistration("Omar", "Tazi", previousCohort.AcademicGroup,
            academicYearId: TestHarness.PreviousYearId);

        // This year's group exists but no cohort was ever configured for it on this stage.
        var orphanGroup = new AcademicGroup
        {
            Id = 77, Label = "Groupe 77", GroupNumber = 77, AcademicYearId = TestHarness.CurrentYearId,
        };
        db.AcademicGroups.Add(orphanGroup);
        var current = db.SeedRegistration("Omar", "Tazi", orphanGroup);
        current.StudentId = previous.StudentId;
        current.Student   = previous.Student;

        db.SeedGradedAssignment(previous, previousCohort, service, mark: 6m, from: new DateOnly(2024, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new RevalidateStageCommand(current.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NoCohortForRevalidation(TestHarness.StageId));
    }
}
