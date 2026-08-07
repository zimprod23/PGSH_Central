using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Delocalization;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Recording a stage served entirely outside the faculty. The handler resolves the student's cohort
// for that stage, replaces the planned in-faculty rotation with one ad-hoc external period, and — when
// the paper verdict is already known — closes the whole thing out in the same step.
public class DelocalizeStudentHandlerTests
{
    private const int ExternalServiceId = 50;

    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private sealed record Scenario(Registration Registration, Cohort Cohort);

    private static async Task<Scenario> SeedAsync(ApplicationDbContext db, bool withAssignment = true)
    {
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "CHU Casablanca — Cardiologie");
        var homeService = db.SeedService(1, "Cardiologie");

        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Omar", "Tazi", cohort.AcademicGroup);

        if (withAssignment)
        {
            var assignment = db.SeedAssignment(registration, cohort);
            db.SeedPeriod(assignment, homeService, Start, End, started: false);
        }

        await db.SaveChangesAsync();
        return new Scenario(registration, cohort);
    }

    private static DelocalizeStudentCommand Command(
        Guid registrationId, EvaluationOutcome? outcome = null, string reason = "Stage effectué à Casablanca") =>
        new(registrationId, TestHarness.StageId, ExternalServiceId, Start, End, reason, outcome);

    private static async Task<InternshipAssignment> LoadAssignmentAsync(ApplicationDbContext db, Guid registrationId) =>
        await db.InternshipAssignments
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Delocalization)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Evaluation)
            .FirstAsync(a => a.RegistrationId == registrationId);

    [Fact]
    public async Task The_planned_rotation_is_replaced_by_one_external_period()
    {
        await using var db = TestHarness.NewContext("deloc-replace");
        var s = await SeedAsync(db);

        var result = await new DelocalizeStudentCommandHandler(db).Handle(Command(s.Registration.Id), default);

        result.IsSuccess.Should().BeTrue();
        var assignment = await LoadAssignmentAsync(db, s.Registration.Id);
        var period = assignment.ServicePeriods.Should().ContainSingle().Subject;
        period.ServiceId.Should().Be(ExternalServiceId);
        period.IsDelocalized.Should().BeTrue();
        period.IsComplete.Should().BeTrue();
        period.Delocalization!.Reason.Should().Be("Stage effectué à Casablanca");
    }

    [Fact]
    public async Task An_assignment_is_created_when_the_student_had_none_for_that_stage()
    {
        await using var db = TestHarness.NewContext("deloc-new");
        var s = await SeedAsync(db, withAssignment: false);

        var result = await new DelocalizeStudentCommandHandler(db).Handle(Command(s.Registration.Id), default);

        result.IsSuccess.Should().BeTrue();
        var assignment = await LoadAssignmentAsync(db, s.Registration.Id);
        assignment.CurrentCohortId.Should().Be(s.Cohort.Id);
        assignment.Status.Should().Be(InternshipStatus.Completed);
    }

    [Fact]
    public async Task A_known_verdict_is_recorded_in_the_same_step()
    {
        await using var db = TestHarness.NewContext("deloc-verdict");
        var s = await SeedAsync(db);

        var result = await new DelocalizeStudentCommandHandler(db)
            .Handle(Command(s.Registration.Id, EvaluationOutcome.Validated), default);

        result.IsSuccess.Should().BeTrue();
        var assignment = await LoadAssignmentAsync(db, s.Registration.Id);
        var evaluation = assignment.ServicePeriods.Single().Evaluation;
        evaluation.Should().NotBeNull();
        evaluation!.Mode.Should().Be(EvaluationMode.ValidatePeriod);
        evaluation.Outcome.Should().Be(EvaluationOutcome.Validated);
        assignment.Result.Should().Be(StageAssignmentResult.Validé);
        assignment.FinalScore.Should().Be(10m);
    }

    [Fact]
    public async Task Without_a_verdict_the_stage_stays_pending_evaluation()
    {
        await using var db = TestHarness.NewContext("deloc-pending");
        var s = await SeedAsync(db);

        await new DelocalizeStudentCommandHandler(db).Handle(Command(s.Registration.Id), default);

        var assignment = await LoadAssignmentAsync(db, s.Registration.Id);
        assignment.ServicePeriods.Single().Evaluation.Should().BeNull();
        assignment.Result.Should().Be(StageAssignmentResult.NonÉvalué);
    }

    [Fact]
    public async Task An_unknown_registration_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("deloc-missing-reg");
        await SeedAsync(db);

        var result = await new DelocalizeStudentCommandHandler(db).Handle(Command(Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.NotFound");
    }

    [Fact]
    public async Task A_student_with_no_group_cannot_be_delocalized()
    {
        await using var db = TestHarness.NewContext("deloc-no-group");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Externe");
        db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Nadia", "Fassi", group: null);
        await db.SaveChangesAsync();

        var result = await new DelocalizeStudentCommandHandler(db).Handle(Command(registration.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NoGroupForDelocalization);
    }

    [Fact]
    public async Task An_unknown_stage_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("deloc-missing-stage");
        var s = await SeedAsync(db);

        var result = await new DelocalizeStudentCommandHandler(db).Handle(
            new DelocalizeStudentCommand(s.Registration.Id, StageId: 999, ExternalServiceId, Start, End, "Motif"),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NotFound(999));
    }

    [Fact]
    public async Task An_unknown_external_service_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("deloc-missing-service");
        var s = await SeedAsync(db);

        var result = await new DelocalizeStudentCommandHandler(db).Handle(
            new DelocalizeStudentCommand(s.Registration.Id, TestHarness.StageId, ServiceId: 999, Start, End, "Motif"),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Services.NotFound");
    }

    [Fact]
    public async Task A_group_with_no_cohort_for_the_stage_is_refused()
    {
        await using var db = TestHarness.NewContext("deloc-no-cohort");
        db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Externe");
        var orphanGroup = new AcademicGroup
        {
            Id = 77, Label = "Groupe 77", GroupNumber = 77, AcademicYearId = TestHarness.CurrentYearId,
        };
        db.AcademicGroups.Add(orphanGroup);
        var registration = db.SeedRegistration("Hamza", "Berrada", orphanGroup);
        await db.SaveChangesAsync();

        var result = await new DelocalizeStudentCommandHandler(db).Handle(Command(registration.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.CohortMissingForStage(TestHarness.StageId));
    }

    [Fact]
    public async Task A_stage_already_running_in_the_faculty_cannot_be_delocalized()
    {
        await using var db = TestHarness.NewContext("deloc-underway");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Externe");
        var homeService = db.SeedService(1, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Salma", "Kabbaj", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        db.SeedPeriod(assignment, homeService, Start, End, started: true);   // already under way
        await db.SaveChangesAsync();

        var result = await new DelocalizeStudentCommandHandler(db).Handle(Command(registration.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.StageAlreadyUnderway);
    }
}
