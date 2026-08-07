using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.Evaluations.Create;
using PGSH.Application.Stages.Evaluations.Update;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Submitting and amending a period's evaluation. Only the chef of that period's service (or an
// administrative user) may act, the period must be closed first, and each of the three modes —
// numeric, validate-the-period, validate-each-objective — must roll up to the right stage verdict.
public class EvaluationHandlerTests
{
    private const int ChefServiceId    = 1;
    private const int ForeignServiceId = 2;

    private static readonly Guid ChefIdentity = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private sealed record Scenario(
        ApplicationDbContext Db, ServicePeriod ChefPeriod, ServicePeriod ForeignPeriod, InternshipAssignment Assignment);

    /// <summary>One closed rotation in the chef's service and one in a service he does not lead.</summary>
    private static async Task<Scenario> SeedAsync(
        ApplicationDbContext db, bool withObjectives = true, bool closePeriods = true)
    {
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var chefService = db.SeedService(ChefServiceId, "Cardiologie", chef);
        var foreignService = db.SeedService(ForeignServiceId, "Réanimation");

        if (withObjectives)
        {
            db.SeedObjective(stage, 1, "Sémiologie", weight: 3, mandatory: true);
            db.SeedObjective(stage, 2, "Gestes techniques", weight: 1);
        }

        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        var chefPeriod = db.SeedPeriod(assignment, chefService, Start, End, complete: closePeriods);
        var foreignPeriod = db.SeedPeriod(assignment, foreignService, Start, End, complete: closePeriods);

        await db.SaveChangesAsync();
        return new Scenario(db, chefPeriod, foreignPeriod, assignment);
    }

    private static CreateServiceEvaluationCommandHandler CreateHandler(ApplicationDbContext db, params string[] roles) =>
        new(db, new ExecutionAuthorizer(db, TestHarness.UserContext(ChefIdentity, roles)));

    private static UpdateServiceEvaluationCommandHandler UpdateHandler(ApplicationDbContext db, params string[] roles) =>
        new(db, new ExecutionAuthorizer(db, TestHarness.UserContext(ChefIdentity, roles)));

    private static CreateServiceEvaluationCommand Numeric(Guid periodId, params (int Id, int Score)[] scores) =>
        new(periodId, EvaluationMode.Numeric, TotalScore: null, Outcome: null, SupervisorComment: "Bon stage",
            scores.Select(s => new ObjectiveScoreRequest(s.Id, s.Score, null, null)).ToList());

    [Fact]
    public async Task The_chef_of_the_service_can_evaluate_a_closed_period()
    {
        await using var db = TestHarness.NewContext("eval-chef");
        var s = await SeedAsync(db);

        var result = await CreateHandler(db).Handle(Numeric(s.ChefPeriod.Id, (1, 16), (2, 12)), default);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.ServiceEvaluation.FirstAsync(e => e.Id == result.Value);
        saved.SupervisorComment.Should().Be("Bon stage");
        saved.EvaluatedAt.Should().NotBeNull("the audit stamp records who graded and when");
    }

    [Fact]
    public async Task The_final_score_is_the_weight_weighted_average_of_the_objectives()
    {
        await using var db = TestHarness.NewContext("eval-weighted");
        var s = await SeedAsync(db);

        // weights 3 and 1 → (16*3 + 12*1) / 4 = 15
        await CreateHandler(db).Handle(Numeric(s.ChefPeriod.Id, (1, 16), (2, 12)), default);

        var assignment = await db.InternshipAssignments.FirstAsync(a => a.Id == s.Assignment.Id);
        assignment.FinalScore.Should().Be(15m);
    }

    [Fact]
    public async Task A_chef_cannot_evaluate_a_period_in_a_service_he_does_not_lead()
    {
        await using var db = TestHarness.NewContext("eval-foreign");
        var s = await SeedAsync(db);

        var result = await CreateHandler(db).Handle(Numeric(s.ForeignPeriod.Id, (1, 16)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NotServiceChef);
        (await db.ServiceEvaluation.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_administrative_user_may_evaluate_any_service()
    {
        await using var db = TestHarness.NewContext("eval-admin");
        var s = await SeedAsync(db);

        var result = await CreateHandler(db, Roles.Scolarite)
            .Handle(Numeric(s.ForeignPeriod.Id, (1, 14)), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_period_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("eval-missing");
        await SeedAsync(db);
        var missing = Guid.NewGuid();

        var result = await CreateHandler(db, Roles.Scolarite).Handle(Numeric(missing, (1, 14)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotFound(missing));
    }

    [Fact]
    public async Task A_period_still_running_cannot_be_evaluated()
    {
        await using var db = TestHarness.NewContext("eval-open");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(ChefServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Ali", "Amrani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        var open = db.SeedPeriod(assignment, service, Start, End, complete: false);
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(Numeric(open.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotComplete(open.Id));
    }

    [Fact]
    public async Task The_same_period_cannot_be_evaluated_twice()
    {
        await using var db = TestHarness.NewContext("eval-twice");
        var s = await SeedAsync(db);
        await CreateHandler(db).Handle(Numeric(s.ChefPeriod.Id, (1, 15)), default);

        var result = await CreateHandler(db).Handle(Numeric(s.ChefPeriod.Id, (1, 18)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.EvaluationAlreadyExists(s.ChefPeriod.Id));
    }

    [Fact]
    public async Task Validate_the_period_mode_stores_a_verdict_and_no_numeric_note()
    {
        await using var db = TestHarness.NewContext("eval-validate-period");
        var s = await SeedAsync(db);

        var result = await CreateHandler(db).Handle(new CreateServiceEvaluationCommand(
            s.ChefPeriod.Id, EvaluationMode.ValidatePeriod, TotalScore: null,
            Outcome: EvaluationOutcome.Validated, SupervisorComment: null, ObjectiveScores: []), default);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.ServiceEvaluation.FirstAsync(e => e.Id == result.Value);
        saved.Outcome.Should().Be(EvaluationOutcome.Validated);
        saved.TotalScore.Should().BeNull("a validate-only evaluation carries no numeric note");
    }

    [Fact]
    public async Task Validate_by_objective_derives_the_period_verdict_from_the_mandatory_ones()
    {
        await using var db = TestHarness.NewContext("eval-validate-objectives");
        var s = await SeedAsync(db);

        // Objective 1 is mandatory and fails → the period fails, whatever the optional one says.
        var result = await CreateHandler(db).Handle(new CreateServiceEvaluationCommand(
            s.ChefPeriod.Id, EvaluationMode.ValidateObjectives, null, null, null,
            [
                new ObjectiveScoreRequest(1, null, EvaluationOutcome.NotValidated, null),
                new ObjectiveScoreRequest(2, null, EvaluationOutcome.Validated, null),
            ]), default);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.ServiceEvaluation.FirstAsync(e => e.Id == result.Value);
        saved.Outcome.Should().Be(EvaluationOutcome.NotValidated);
    }

    [Fact]
    public async Task The_stage_verdict_is_withheld_until_every_period_is_evaluated()
    {
        await using var db = TestHarness.NewContext("eval-partial");
        var s = await SeedAsync(db);

        await CreateHandler(db).Handle(Numeric(s.ChefPeriod.Id, (1, 16), (2, 16)), default);

        var assignment = await db.InternshipAssignments.FirstAsync(a => a.Id == s.Assignment.Id);
        assignment.FinalScore.Should().Be(16m, "a partial mean is still shown");
        assignment.Result.Should().Be(StageAssignmentResult.NonÉvalué, "the second period has no evaluation yet");
    }

    [Fact]
    public async Task Amending_an_evaluation_recomputes_the_stage_note()
    {
        await using var db = TestHarness.NewContext("eval-update");
        var s = await SeedAsync(db, withObjectives: false);
        var created = await CreateHandler(db).Handle(new CreateServiceEvaluationCommand(
            s.ChefPeriod.Id, EvaluationMode.Numeric, TotalScore: 11m, Outcome: null,
            SupervisorComment: null, ObjectiveScores: []), default);
        created.IsSuccess.Should().BeTrue();

        var result = await UpdateHandler(db).Handle(new UpdateServiceEvaluationCommand(
            created.Value, EvaluationMode.Numeric, TotalScore: 17m, Outcome: null,
            SupervisorComment: "Révisé", ObjectiveScores: []), default);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.ServiceEvaluation.FirstAsync(e => e.Id == created.Value);
        saved.TotalScore.Should().Be(17m);
        saved.SupervisorComment.Should().Be("Révisé");
        (await db.InternshipAssignments.FirstAsync(a => a.Id == s.Assignment.Id)).FinalScore.Should().Be(17m);
    }

    [Fact]
    public async Task A_chef_cannot_amend_an_evaluation_of_a_foreign_service()
    {
        await using var db = TestHarness.NewContext("eval-update-foreign");
        var s = await SeedAsync(db, withObjectives: false);
        var created = await CreateHandler(db, Roles.Scolarite).Handle(new CreateServiceEvaluationCommand(
            s.ForeignPeriod.Id, EvaluationMode.Numeric, 12m, null, null, []), default);

        var result = await UpdateHandler(db).Handle(new UpdateServiceEvaluationCommand(
            created.Value, EvaluationMode.Numeric, 19m, null, null, []), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NotServiceChef);
    }

    [Fact]
    public async Task An_evaluation_becomes_read_only_once_the_stage_is_validated()
    {
        await using var db = TestHarness.NewContext("eval-readonly");
        // Drive the real lifecycle rather than seeding closed periods: only an assignment that
        // reached Completed through CompletePeriod can roll up to Evaluated, which Validate() needs.
        var s = await SeedAsync(db, withObjectives: false, closePeriods: false);
        s.Assignment.Start().IsSuccess.Should().BeTrue();
        foreach (var period in s.Assignment.ServicePeriods.ToList())
            s.Assignment.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var first  = await CreateHandler(db).Handle(new CreateServiceEvaluationCommand(
            s.ChefPeriod.Id, EvaluationMode.Numeric, 15m, null, null, []), default);
        await CreateHandler(db, Roles.Scolarite).Handle(new CreateServiceEvaluationCommand(
            s.ForeignPeriod.Id, EvaluationMode.Numeric, 13m, null, null, []), default);

        var assignment = await db.InternshipAssignments
            .Include(a => a.ServicePeriods).FirstAsync(a => a.Id == s.Assignment.Id);
        assignment.Status.Should().Be(InternshipStatus.Evaluated);
        assignment.Validate().IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var result = await UpdateHandler(db).Handle(new UpdateServiceEvaluationCommand(
            first.Value, EvaluationMode.Numeric, 20m, null, null, []), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.EvaluationReadOnly(InternshipStatus.Validated));
    }

    [Fact]
    public async Task Amending_an_unknown_evaluation_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("eval-update-missing");
        await SeedAsync(db);
        var missing = Guid.NewGuid();

        var result = await UpdateHandler(db, Roles.Scolarite).Handle(new UpdateServiceEvaluationCommand(
            missing, EvaluationMode.Numeric, 12m, null, null, []), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.EvaluationNotFound(missing));
    }
}
