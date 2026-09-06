using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Delocalization;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Application.Employees.MyServices;
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
        new(registrationId, TestHarness.StageId, ExternalServiceId, reason, Start, End,
            outcome is null ? null : new DelocalizationVerdict(EvaluationMode.ValidatePeriod, Outcome: outcome));

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

        var result = await db.DelocalizeHandler().Handle(Command(s.Registration.Id), default);

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

        var result = await db.DelocalizeHandler().Handle(Command(s.Registration.Id), default);

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

        var result = await db.DelocalizeHandler()
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

        await db.DelocalizeHandler().Handle(Command(s.Registration.Id), default);

        var assignment = await LoadAssignmentAsync(db, s.Registration.Id);
        assignment.ServicePeriods.Single().Evaluation.Should().BeNull();
        assignment.Result.Should().Be(StageAssignmentResult.NonÉvalué);
    }

    [Fact]
    public async Task An_unknown_registration_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("deloc-missing-reg");
        await SeedAsync(db);

        var result = await db.DelocalizeHandler().Handle(Command(Guid.NewGuid()), default);

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

        var result = await db.DelocalizeHandler().Handle(Command(registration.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NoGroupForDelocalization);
    }

    [Fact]
    public async Task An_unknown_stage_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("deloc-missing-stage");
        var s = await SeedAsync(db);

        var result = await db.DelocalizeHandler().Handle(
            new DelocalizeStudentCommand(s.Registration.Id, StageId: 999, ExternalServiceId, "Motif", Start, End),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NotFound(999));
    }

    [Fact]
    public async Task An_unknown_external_service_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("deloc-missing-service");
        var s = await SeedAsync(db);

        var result = await db.DelocalizeHandler().Handle(
            new DelocalizeStudentCommand(s.Registration.Id, TestHarness.StageId, ServiceId: 999, "Motif", Start, End),
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

        var result = await db.DelocalizeHandler().Handle(Command(registration.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.CohortMissingForStage(TestHarness.StageId));
    }

    // ⚠ The rule this asserts was the opposite until 2026-09-06: any started period refused the
    // délocalisation. A student who leaves for an external hospital mid-rotation is the ordinary
    // case — the faculty's dates are a formality the place he goes to does not follow — so a started
    // period is dropped like a planned one. What may not be overwritten is a mark.
    [Fact]
    public async Task A_rotation_already_under_way_is_dropped_rather_than_refused()
    {
        await using var db = TestHarness.NewContext("deloc-underway");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Externe", isExternal: true);
        var homeService = db.SeedService(1, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Salma", "Kabbaj", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        db.SeedPeriod(assignment, homeService, Start, End, started: true);   // already under way
        await db.SaveChangesAsync();

        var result = await db.DelocalizeHandler().Handle(Command(registration.Id), default);

        result.IsSuccess.Should().BeTrue();
        var saved = await LoadAssignmentAsync(db, registration.Id);
        var period = saved.ServicePeriods.Should().ContainSingle().Subject;
        period.ServiceId.Should().Be(ExternalServiceId);
        period.IsDelocalized.Should().BeTrue();
    }

    [Fact]
    public async Task A_stage_carrying_a_mark_is_refused_so_the_note_cannot_be_erased()
    {
        await using var db = TestHarness.NewContext("deloc-marked");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Externe", isExternal: true);
        var homeService = db.SeedService(1, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Youssef", "Alami", cohort.AcademicGroup);
        db.SeedGradedAssignment(registration, cohort, homeService, mark: 14m);
        await db.SaveChangesAsync();

        var result = await db.DelocalizeHandler().Handle(Command(registration.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.DelocalizationOverMark);

        // ⚠ And nothing was written. A guard ordered after the write returns the same failure.
        var saved = await LoadAssignmentAsync(db, registration.Id);
        saved.ServicePeriods.Should().ContainSingle().Which.ServiceId.Should().Be(1);
    }

    [Fact]
    public async Task Omitted_dates_fall_back_to_the_stage_window_for_that_promotion()
    {
        await using var db = TestHarness.NewContext("deloc-window");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Externe", isExternal: true);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Imane", "Rachidi", cohort.AcademicGroup);
        db.SeedSlot(stage, 1, 1, new DateOnly(2025, 10, 6), new DateOnly(2025, 11, 2));
        db.SeedSlot(stage, 2, 2, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 30));
        await db.SaveChangesAsync();

        var result = await db.DelocalizeHandler().Handle(
            new DelocalizeStudentCommand(registration.Id, TestHarness.StageId, ExternalServiceId, "Kénitra"),
            default);

        result.IsSuccess.Should().BeTrue();
        var period = (await LoadAssignmentAsync(db, registration.Id)).ServicePeriods.Single();
        period.StartDate.Should().Be(new DateOnly(2025, 10, 6));
        period.EndDate.Should().Be(new DateOnly(2025, 11, 30));
    }

    // ⚠ Says what the blank means. A stage whose grid was never authored has no window at all —
    // every imported year is in that state — and inventing a pair of dates would put a fabricated
    // fact in the dossier looking exactly like a recorded one.
    [Fact]
    public async Task Omitted_dates_on_a_stage_with_no_creneaux_are_refused_by_name()
    {
        await using var db = TestHarness.NewContext("deloc-no-window");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Externe", isExternal: true);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Reda", "Squalli", cohort.AcademicGroup);
        await db.SaveChangesAsync();

        var result = await db.DelocalizeHandler().Handle(
            new DelocalizeStudentCommand(registration.Id, TestHarness.StageId, ExternalServiceId, "Kénitra"),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Delocalizations.NoWindow");
    }

    // The external hospital sends back whatever it sends back. A note /20 used to be flattened to
    // « validé » on the way in, so the number the student earned existed nowhere.
    [Fact]
    public async Task A_numeric_verdict_keeps_the_note_the_external_service_gave()
    {
        await using var db = TestHarness.NewContext("deloc-numeric");
        var s = await SeedAsync(db);

        var result = await db.DelocalizeHandler().Handle(
            new DelocalizeStudentCommand(
                s.Registration.Id, TestHarness.StageId, ExternalServiceId, "Kénitra", Start, End,
                new DelocalizationVerdict(EvaluationMode.Numeric, TotalScore: 15.5m, FicheReference: "FICHE-42")),
            default);

        result.IsSuccess.Should().BeTrue();
        var evaluation = (await LoadAssignmentAsync(db, s.Registration.Id)).ServicePeriods.Single().Evaluation;
        evaluation!.Mode.Should().Be(EvaluationMode.Numeric);
        evaluation.TotalScore.Should().Be(15.5m);
        evaluation.FicheReference.Should().Be("FICHE-42");
    }

    [Fact]
    public async Task A_delocalization_can_be_cancelled_and_the_student_returns_to_the_repartition()
    {
        await using var db = TestHarness.NewContext("deloc-cancel");
        var s = await SeedAsync(db);
        await db.DelocalizeHandler().Handle(Command(s.Registration.Id), default);

        var result = await new CancelDelocalizationCommandHandler(db, db.AdminAuthorizer())
            .Handle(new CancelDelocalizationCommand(s.Registration.Id, TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();
        var saved = await LoadAssignmentAsync(db, s.Registration.Id);
        saved.ServicePeriods.Should().BeEmpty();
        saved.Status.Should().Be(InternshipStatus.Planned);
    }

    [Fact]
    public async Task Cancelling_is_refused_once_the_paper_verdict_is_recorded()
    {
        await using var db = TestHarness.NewContext("deloc-cancel-marked");
        var s = await SeedAsync(db);
        await db.DelocalizeHandler().Handle(Command(s.Registration.Id, EvaluationOutcome.Validated), default);

        var result = await new CancelDelocalizationCommandHandler(db, db.AdminAuthorizer())
            .Handle(new CancelDelocalizationCommand(s.Registration.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.DelocalizationAlreadyMarked);
        (await LoadAssignmentAsync(db, s.Registration.Id)).ServicePeriods.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancelling_a_stage_that_was_never_delocalized_is_refused()
    {
        await using var db = TestHarness.NewContext("deloc-cancel-none");
        var s = await SeedAsync(db);

        var result = await new CancelDelocalizationCommandHandler(db, db.AdminAuthorizer())
            .Handle(new CancelDelocalizationCommand(s.Registration.Id, TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NotDelocalized);
    }
}
