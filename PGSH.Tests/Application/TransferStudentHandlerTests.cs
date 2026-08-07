using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.Transfer;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Moving a student between groups. A Definitive move changes the registration's group so every stage
// follows; a Temporary loan touches only the named stage's assignment and leaves the registration
// where it is, so the student's other stages still run with their original group.
public class TransferStudentHandlerTests
{
    private const int OriginGroupId = 10;
    private const int TargetGroupId = 20;
    private const int ServiceId     = 1;

    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private sealed record Scenario(Registration Registration, Cohort Origin, Cohort Target);

    private static async Task<Scenario> SeedAsync(ApplicationDbContext db, bool withPeriod = true)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");

        var origin = db.SeedCohort(stage, OriginGroupId, "Groupe 10");
        var target = db.SeedCohort(stage, TargetGroupId, "Groupe 20");

        var registration = db.SeedRegistration("Imane", "Chraibi", origin.AcademicGroup);
        var assignment = db.SeedAssignment(registration, origin);
        if (withPeriod)
            db.SeedPeriod(assignment, service, Start, End, started: false);

        await db.SaveChangesAsync();
        return new Scenario(registration, origin, target);
    }

    private static TransferStudentCommandHandler Handler(ApplicationDbContext db) =>
        new(db, new MidStageTransferRescheduler(db));

    private static async Task<InternshipAssignment> LoadAssignmentAsync(ApplicationDbContext db, Guid registrationId) =>
        await db.InternshipAssignments
            .Include(a => a.MembershipHistory)
            .Include(a => a.ServicePeriods)
            .FirstAsync(a => a.RegistrationId == registrationId);

    [Fact]
    public async Task A_definitive_transfer_moves_the_registration_and_the_assignment()
    {
        await using var db = TestHarness.NewContext("transfer-definitive");
        var s = await SeedAsync(db);

        var result = await Handler(db).Handle(new TransferStudentCommand(
            s.Registration.Id, TargetGroupId, "Rapprochement familial", TransferType.Definitive), default);

        result.IsSuccess.Should().BeTrue();
        (await db.Registrations.FirstAsync(r => r.Id == s.Registration.Id))
            .AcademicGroupId.Should().Be(TargetGroupId, "every stage follows a definitive move");
        (await LoadAssignmentAsync(db, s.Registration.Id)).CurrentCohortId.Should().Be(s.Target.Id);
    }

    [Fact]
    public async Task A_definitive_transfer_appends_to_the_membership_trail()
    {
        await using var db = TestHarness.NewContext("transfer-trail");
        var s = await SeedAsync(db);

        await Handler(db).Handle(new TransferStudentCommand(
            s.Registration.Id, TargetGroupId, "Motif", TransferType.Definitive), default);

        var assignment = await LoadAssignmentAsync(db, s.Registration.Id);
        assignment.MembershipHistory.Should().HaveCount(2);
        assignment.MembershipHistory.Single(m => m.CohortId == s.Origin.Id).EndDate.Should().NotBeNull();
        var open = assignment.MembershipHistory.Single(m => m.EndDate is null);
        open.CohortId.Should().Be(s.Target.Id);
        open.TransferReason.Should().Be("Motif");
    }

    [Fact]
    public async Task A_temporary_loan_leaves_the_registration_group_untouched()
    {
        await using var db = TestHarness.NewContext("transfer-temporary");
        var s = await SeedAsync(db);

        var result = await Handler(db).Handle(new TransferStudentCommand(
            s.Registration.Id, TargetGroupId, "Prêt", TransferType.Temporary, StageId: TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();
        (await db.Registrations.FirstAsync(r => r.Id == s.Registration.Id))
            .AcademicGroupId.Should().Be(OriginGroupId, "only the named stage moves");
        var assignment = await LoadAssignmentAsync(db, s.Registration.Id);
        assignment.CurrentCohortId.Should().Be(s.Target.Id);
        assignment.MembershipHistory.Single(m => m.EndDate is null)
            .OriginalCohortId.Should().Be(s.Origin.Id, "a loan remembers where to return");
    }

    [Fact]
    public async Task A_definitive_transfer_to_the_group_the_student_is_already_in_is_refused()
    {
        await using var db = TestHarness.NewContext("transfer-same");
        var s = await SeedAsync(db);

        var result = await Handler(db).Handle(new TransferStudentCommand(
            s.Registration.Id, OriginGroupId, "Motif", TransferType.Definitive), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.SameGroup");
    }

    [Fact]
    public async Task An_unknown_registration_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("transfer-missing-reg");
        await SeedAsync(db);

        var result = await Handler(db).Handle(new TransferStudentCommand(
            Guid.NewGuid(), TargetGroupId, "Motif", TransferType.Definitive), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.NotFound");
    }

    [Fact]
    public async Task An_unknown_target_group_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("transfer-missing-group");
        var s = await SeedAsync(db);

        var result = await Handler(db).Handle(new TransferStudentCommand(
            s.Registration.Id, TargetGroupId: 999, "Motif", TransferType.Definitive), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.NotFound");
    }

    [Fact]
    public async Task A_temporary_loan_needs_an_active_assignment_for_the_named_stage()
    {
        await using var db = TestHarness.NewContext("transfer-no-assignment");
        var stage = db.SeedCatalog();
        db.SeedCohort(stage, OriginGroupId, "Groupe 10");
        var target = db.SeedCohort(stage, TargetGroupId, "Groupe 20");
        var registration = db.SeedRegistration("Karim", "Alami", target.AcademicGroup);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new TransferStudentCommand(
            registration.Id, TargetGroupId, "Prêt", TransferType.Temporary, StageId: TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Transfers.NoActiveAssignment");
    }

    [Fact]
    public async Task A_validated_stage_is_never_moved_by_a_transfer()
    {
        await using var db = TestHarness.NewContext("transfer-validated");
        var s = await SeedAsync(db, withPeriod: false);
        var assignment = await db.InternshipAssignments
            .Include(a => a.ServicePeriods).FirstAsync(a => a.RegistrationId == s.Registration.Id);

        var service = await db.Services.FirstAsync();
        var period = db.SeedPeriod(assignment, service, Start, End);
        assignment.Start();
        assignment.CompletePeriod(period.Id);
        assignment.SubmitEvaluation(period.Id, new ServiceEvaluation { Mode = EvaluationMode.Numeric, TotalScore = 15m });
        assignment.Validate().IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        await Handler(db).Handle(new TransferStudentCommand(
            s.Registration.Id, TargetGroupId, "Motif", TransferType.Definitive), default);

        (await LoadAssignmentAsync(db, s.Registration.Id))
            .CurrentCohortId.Should().Be(s.Origin.Id, "a closed-out stage keeps its history");
    }

    [Fact]
    public async Task The_transfer_reason_reaches_the_domain_event()
    {
        await using var db = TestHarness.NewContext("transfer-event");
        var s = await SeedAsync(db);

        await Handler(db).Handle(new TransferStudentCommand(
            s.Registration.Id, TargetGroupId, "Motif médical", TransferType.Definitive), default);

        // The assignment is still tracked, so its pending events are observable before dispatch.
        var assignment = db.ChangeTracker.Entries<InternshipAssignment>().Single().Entity;
        assignment.DomainEvents.OfType<StudentCohortTransferredDomainEvent>()
            .Should().ContainSingle().Which.Reason.Should().Be("Motif médical");
    }
}
