using FluentAssertions;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Stages.Delocalization;
using PGSH.Application.Stages.Evaluations.GetByPeriod;
using PGSH.Application.Stages.InternshipAssignments.Fiche;
using PGSH.Application.Stages.InternshipAssignments.GetRecord;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Who may read a student's marks, and who may record a délocalisation. Assignment and period ids are
// guessable, so before these guards existed any authenticated user could walk the ids and read every
// classmate's file — and a student could delocalize their own registration with outcome Validated and
// pass their own stage.
public class ReadScopeTests
{
    private const int ChefServiceId    = 1;
    private const int ForeignServiceId = 2;

    private static readonly Guid ChefIdentity      = Guid.NewGuid();
    private static readonly Guid OwnerIdentity     = Guid.NewGuid();
    private static readonly Guid ClassmateIdentity = Guid.NewGuid();

    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private sealed record Scenario(InternshipAssignment Assignment, ServicePeriod Period, Guid RegistrationId);

    /// <summary>One student's rotation in the chef's service, plus an unrelated classmate.</summary>
    private static async Task<Scenario> SeedAsync(
        ApplicationDbContext db, int serviceId = ChefServiceId, bool started = true)
    {
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var chefService = db.SeedService(ChefServiceId, "Cardiologie", chef);
        var otherService = db.SeedService(ForeignServiceId, "Réanimation");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");

        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        registration.Student.LinkIdentity(OwnerIdentity.ToString());
        var assignment = db.SeedAssignment(registration, cohort);
        var period = db.SeedPeriod(
            assignment, serviceId == ChefServiceId ? chefService : otherService, Start, End, started);

        var classmate = db.SeedRegistration("Ali", "Amrani", cohort.AcademicGroup);
        classmate.Student.LinkIdentity(ClassmateIdentity.ToString());

        await db.SaveChangesAsync();
        return new Scenario(assignment, period, registration.Id);
    }

    private static ExecutionAuthorizer As(ApplicationDbContext db, Guid identity, params string[] roles) =>
        new(db, TestHarness.UserContext(identity, roles));

    private static GetStudentStageRecordQueryHandler Record(ApplicationDbContext db, ExecutionAuthorizer auth) =>
        new(db, auth);

    // ─── Stage record ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_student_can_read_their_own_stage_record()
    {
        await using var db = TestHarness.NewContext("scope-record-owner");
        var s = await SeedAsync(db);

        var result = await Record(db, As(db, OwnerIdentity, Roles.Student))
            .Handle(new GetStudentStageRecordQuery(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_classmate_cannot_read_someone_elses_stage_record()
    {
        await using var db = TestHarness.NewContext("scope-record-classmate");
        var s = await SeedAsync(db);

        var result = await Record(db, As(db, ClassmateIdentity, Roles.Student))
            .Handle(new GetStudentStageRecordQuery(s.Assignment.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AssignmentReadNotAllowed);
    }

    [Fact]
    public async Task The_chef_of_a_service_the_student_rotates_through_can_read_the_record()
    {
        await using var db = TestHarness.NewContext("scope-record-chef");
        var s = await SeedAsync(db);

        var result = await Record(db, As(db, ChefIdentity))
            .Handle(new GetStudentStageRecordQuery(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_chef_of_an_unrelated_service_cannot_read_the_record()
    {
        await using var db = TestHarness.NewContext("scope-record-foreignchef");
        var s = await SeedAsync(db, serviceId: ForeignServiceId);

        var result = await Record(db, As(db, ChefIdentity))
            .Handle(new GetStudentStageRecordQuery(s.Assignment.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AssignmentReadNotAllowed);
    }

    [Fact]
    public async Task An_unknown_assignment_still_reports_not_found_rather_than_forbidden()
    {
        await using var db = TestHarness.NewContext("scope-record-missing");
        await SeedAsync(db);
        var missing = Guid.NewGuid();

        var result = await Record(db, As(db, ClassmateIdentity, Roles.Student))
            .Handle(new GetStudentStageRecordQuery(missing), default);

        result.Error.Should().Be(StageErrors.AssignmentNotFound(missing));
    }

    // ─── Fiche ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_classmate_cannot_pull_someone_elses_fiche()
    {
        await using var db = TestHarness.NewContext("scope-fiche-classmate");
        var s = await SeedAsync(db);

        var result = await new GetFicheDeValidationQueryHandler(db, As(db, ClassmateIdentity, Roles.Student))
            .Handle(new GetFicheDeValidationQuery(s.Assignment.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AssignmentReadNotAllowed);
    }

    // ─── Evaluation read ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_student_can_read_the_evaluation_of_their_own_rotation()
    {
        await using var db = TestHarness.NewContext("scope-eval-owner");
        var s = await SeedAsync(db);
        s.Assignment.CompletePeriod(s.Period.Id).IsSuccess.Should().BeTrue();
        s.Assignment.SubmitEvaluation(s.Period.Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.Numeric, TotalScore = 14m,
        }).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var result = await new GetServiceEvaluationQueryHandler(db, As(db, OwnerIdentity, Roles.Student))
            .Handle(new GetServiceEvaluationQuery(s.Period.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalScore.Should().Be(14m);
    }

    [Fact]
    public async Task A_classmate_cannot_read_the_evaluation_of_a_rotation_that_is_not_theirs()
    {
        await using var db = TestHarness.NewContext("scope-eval-classmate");
        var s = await SeedAsync(db);

        var result = await new GetServiceEvaluationQueryHandler(db, As(db, ClassmateIdentity, Roles.Student))
            .Handle(new GetServiceEvaluationQuery(s.Period.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.EvaluationReadNotAllowed);
    }

    // ─── Délocalisation ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_student_cannot_delocalize_their_own_registration()
    {
        await using var db = TestHarness.NewContext("scope-deloc-student");
        // Délocalisation is only possible before the in-faculty rotation begins.
        var s = await SeedAsync(db, started: false);

        // The shape of the attack: post your own registration with a passing verdict.
        var result = await new DelocalizeStudentCommandHandler(db, As(db, OwnerIdentity, Roles.Student))
            .Handle(new DelocalizeStudentCommand(
                s.RegistrationId, TestHarness.StageId, ForeignServiceId, Start, End,
                "Stage à l'étranger", EvaluationOutcome.Validated), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.DelocalizationNotAllowed);
    }

    [Fact]
    public async Task A_service_chef_cannot_delocalize_either()
    {
        await using var db = TestHarness.NewContext("scope-deloc-chef");
        var s = await SeedAsync(db, started: false);

        var result = await new DelocalizeStudentCommandHandler(db, As(db, ChefIdentity))
            .Handle(new DelocalizeStudentCommand(
                s.RegistrationId, TestHarness.StageId, ForeignServiceId, Start, End, "Motif"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.DelocalizationNotAllowed);
    }

    [Fact]
    public async Task Scolarite_may_delocalize()
    {
        await using var db = TestHarness.NewContext("scope-deloc-admin");
        var s = await SeedAsync(db, started: false);

        var result = await new DelocalizeStudentCommandHandler(db, As(db, Guid.NewGuid(), Roles.Scolarite))
            .Handle(new DelocalizeStudentCommand(
                s.RegistrationId, TestHarness.StageId, ForeignServiceId, Start, End, "Motif"), default);

        result.IsSuccess.Should().BeTrue();
    }
}
