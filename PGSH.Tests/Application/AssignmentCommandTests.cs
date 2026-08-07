using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.InternshipAssignments.Reject;
using PGSH.Application.Stages.InternshipAssignments.Start;
using PGSH.Application.Stages.InternshipAssignments.Validate;
using PGSH.Application.Stages.ServicePeriods.Complete;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The per-assignment admin actions: start one student's rotation, close one period, and record the
// terminal verdict. Each surfaces the domain's guard as a Result rather than throwing, and closing a
// period is service-scoped like the evaluation it precedes.
public class AssignmentCommandTests
{
    private const int ChefServiceId    = 1;
    private const int ForeignServiceId = 2;

    private static readonly Guid ChefIdentity = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private sealed record Scenario(
        InternshipAssignment Assignment, ServicePeriod ChefPeriod, ServicePeriod ForeignPeriod);

    private static async Task<Scenario> SeedAsync(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var chefService = db.SeedService(ChefServiceId, "Cardiologie", chef);
        var foreignService = db.SeedService(ForeignServiceId, "Réanimation");

        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        var chefPeriod = db.SeedPeriod(assignment, chefService, Start, End, started: false);
        var foreignPeriod = db.SeedPeriod(assignment, foreignService, Start, End, started: false);

        await db.SaveChangesAsync();
        return new Scenario(assignment, chefPeriod, foreignPeriod);
    }

    private static CompleteServicePeriodCommandHandler CloseHandler(ApplicationDbContext db, params string[] roles) =>
        new(db, new ExecutionAuthorizer(db, TestHarness.UserContext(ChefIdentity, roles)));

    private static async Task<InternshipAssignment> ReloadAsync(ApplicationDbContext db, Guid id) =>
        await db.InternshipAssignments.Include(a => a.ServicePeriods).FirstAsync(a => a.Id == id);

    [Fact]
    public async Task Starting_an_assignment_activates_all_of_its_rotations()
    {
        await using var db = TestHarness.NewContext("cmd-start");
        var s = await SeedAsync(db);

        var result = await new StartAssignmentCommandHandler(db)
            .Handle(new StartAssignmentCommand(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
        var assignment = await ReloadAsync(db, s.Assignment.Id);
        assignment.Status.Should().Be(InternshipStatus.Ongoing);
        assignment.ServicePeriods.Should().OnlyContain(p => p.IsStarted);
    }

    [Fact]
    public async Task Starting_an_unknown_assignment_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("cmd-start-missing");
        await SeedAsync(db);
        var missing = Guid.NewGuid();

        var result = await new StartAssignmentCommandHandler(db)
            .Handle(new StartAssignmentCommand(missing), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AssignmentNotFound(missing));
    }

    [Fact]
    public async Task Starting_an_already_running_assignment_surfaces_the_domain_guard()
    {
        await using var db = TestHarness.NewContext("cmd-start-twice");
        var s = await SeedAsync(db);
        await new StartAssignmentCommandHandler(db).Handle(new StartAssignmentCommand(s.Assignment.Id), default);

        var result = await new StartAssignmentCommandHandler(db)
            .Handle(new StartAssignmentCommand(s.Assignment.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.InvalidStatusTransition("Start", InternshipStatus.Ongoing));
    }

    [Fact]
    public async Task The_chef_of_the_service_closes_his_own_rotation()
    {
        await using var db = TestHarness.NewContext("cmd-close");
        var s = await SeedAsync(db);
        await new StartAssignmentCommandHandler(db).Handle(new StartAssignmentCommand(s.Assignment.Id), default);

        var result = await CloseHandler(db).Handle(new CompleteServicePeriodCommand(s.ChefPeriod.Id), default);

        result.IsSuccess.Should().BeTrue();
        (await ReloadAsync(db, s.Assignment.Id)).ServicePeriods
            .Single(p => p.Id == s.ChefPeriod.Id).IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task A_rotation_in_a_foreign_service_cannot_be_closed()
    {
        await using var db = TestHarness.NewContext("cmd-close-foreign");
        var s = await SeedAsync(db);
        await new StartAssignmentCommandHandler(db).Handle(new StartAssignmentCommand(s.Assignment.Id), default);

        var result = await CloseHandler(db).Handle(new CompleteServicePeriodCommand(s.ForeignPeriod.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NotServiceChef);
    }

    [Fact]
    public async Task Closing_the_last_rotation_completes_the_whole_stage()
    {
        await using var db = TestHarness.NewContext("cmd-close-last");
        var s = await SeedAsync(db);
        await new StartAssignmentCommandHandler(db).Handle(new StartAssignmentCommand(s.Assignment.Id), default);

        await CloseHandler(db, Roles.Scolarite).Handle(new CompleteServicePeriodCommand(s.ChefPeriod.Id), default);
        (await ReloadAsync(db, s.Assignment.Id)).Status.Should().Be(InternshipStatus.Ongoing);

        await CloseHandler(db, Roles.Scolarite).Handle(new CompleteServicePeriodCommand(s.ForeignPeriod.Id), default);

        (await ReloadAsync(db, s.Assignment.Id)).Status.Should().Be(InternshipStatus.Completed);
    }

    // NOTE: closing a rotation that never started is currently accepted — CompletePeriod guards
    // interrupted/complete/paused but not unstarted, unlike PausePeriod which does. Deliberately left
    // uncovered: a test either way would cement an asymmetry that has not been ruled on yet.

    [Fact]
    public async Task Closing_an_unknown_rotation_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("cmd-close-missing");
        await SeedAsync(db);
        var missing = Guid.NewGuid();

        var result = await CloseHandler(db, Roles.Scolarite)
            .Handle(new CompleteServicePeriodCommand(missing), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotFound(missing));
    }

    [Fact]
    public async Task Closing_the_stage_of_a_loaned_student_sends_him_home()
    {
        await using var db = TestHarness.NewContext("cmd-close-loan");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(ChefServiceId, "Cardiologie", chef);
        var origin = db.SeedCohort(stage, 10, "Groupe 10");
        var target = db.SeedCohort(stage, 20, "Groupe 20");
        var registration = db.SeedRegistration("Ali", "Amrani", origin.AcademicGroup);
        var assignment = db.SeedAssignment(registration, origin);
        var period = db.SeedPeriod(assignment, service, Start, End);
        assignment.Start().IsSuccess.Should().BeTrue();
        assignment.TransferToCohort(target.Id, "Prêt", new DateOnly(2026, 3, 5), TransferType.Temporary);
        await db.SaveChangesAsync();

        var result = await CloseHandler(db).Handle(new CompleteServicePeriodCommand(period.Id), default);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await db.InternshipAssignments
            .Include(a => a.MembershipHistory).FirstAsync(a => a.Id == assignment.Id);
        reloaded.MembershipHistory.Single(m => m.CohortId == target.Id).EndDate
            .Should().NotBeNull("the loan ends with the stage it was made for");
    }

    [Fact]
    public async Task A_fully_evaluated_stage_can_be_validated_by_the_administration()
    {
        await using var db = TestHarness.NewContext("cmd-validate");
        var s = await SeedAsync(db);
        await EvaluateEverythingAsync(db, s);

        var result = await new ValidateAssignmentCommandHandler(db)
            .Handle(new ValidateAssignmentCommand(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
        var assignment = await ReloadAsync(db, s.Assignment.Id);
        assignment.Status.Should().Be(InternshipStatus.Validated);
        assignment.Result.Should().Be(StageAssignmentResult.Validé);
    }

    [Fact]
    public async Task Refusing_to_ratify_moves_the_workflow_without_rewriting_the_marks()
    {
        await using var db = TestHarness.NewContext("cmd-reject");
        var s = await SeedAsync(db);
        await EvaluateEverythingAsync(db, s);

        var result = await new RejectAssignmentCommandHandler(db)
            .Handle(new RejectAssignmentCommand(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
        var assignment = await ReloadAsync(db, s.Assignment.Id);
        assignment.Status.Should().Be(InternshipStatus.Rejected);
        assignment.Result.Should().Be(StageAssignmentResult.Validé,
            "refusing to officialise an evaluation does not retroactively fail the student");
    }

    [Fact]
    public async Task Ratifying_a_failed_stage_keeps_it_failed()
    {
        await using var db = TestHarness.NewContext("cmd-validate-failed");
        var s = await SeedAsync(db);
        await EvaluateEverythingAsync(db, s, mark: 7m);

        var result = await new ValidateAssignmentCommandHandler(db)
            .Handle(new ValidateAssignmentCommand(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
        var assignment = await ReloadAsync(db, s.Assignment.Id);
        assignment.Status.Should().Be(InternshipStatus.Validated);
        assignment.Result.Should().Be(StageAssignmentResult.NonValidé,
            "'Valider' officialises the chef's verdict — it is not an academic override");
        assignment.FinalScore.Should().Be(7m);
    }

    [Fact]
    public async Task A_stage_still_running_cannot_be_validated()
    {
        await using var db = TestHarness.NewContext("cmd-validate-early");
        var s = await SeedAsync(db);
        await new StartAssignmentCommandHandler(db).Handle(new StartAssignmentCommand(s.Assignment.Id), default);

        var result = await new ValidateAssignmentCommandHandler(db)
            .Handle(new ValidateAssignmentCommand(s.Assignment.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.InvalidStatusTransition("Validate", InternshipStatus.Ongoing));
    }

    [Fact]
    public async Task Validating_an_unknown_assignment_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("cmd-validate-missing");
        await SeedAsync(db);
        var missing = Guid.NewGuid();

        var result = await new ValidateAssignmentCommandHandler(db)
            .Handle(new ValidateAssignmentCommand(missing), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AssignmentNotFound(missing));
    }

    /// <summary>Drives the assignment to Evaluated — the only state ratification accepts.</summary>
    private static async Task EvaluateEverythingAsync(ApplicationDbContext db, Scenario s, decimal mark = 14m)
    {
        var assignment = await ReloadAsync(db, s.Assignment.Id);
        assignment.Start().IsSuccess.Should().BeTrue();
        foreach (var period in assignment.ServicePeriods.ToList())
        {
            assignment.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();
            assignment.SubmitEvaluation(period.Id, new ServiceEvaluation
            {
                Mode = EvaluationMode.Numeric, TotalScore = mark,
            }).IsSuccess.Should().BeTrue();
        }
        assignment.Status.Should().Be(InternshipStatus.Evaluated);
        await db.SaveChangesAsync();
    }
}
