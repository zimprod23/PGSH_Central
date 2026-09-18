using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.GroupChange;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using PGSH.SharedKernel;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Changement de groupe » — the move that leaves no trace, and the échange built out of two of them.
///
/// <para>It is the third act on a roster and the only one that is a <b>correction</b>: joining puts a
/// student where he was not, transferring moves him and says so, this one says the record was wrong.
/// Everything asserted here follows from that one difference — the membership row is rewritten instead
/// of closed, no domain event is raised, and the whole act is refused the moment something on record
/// contradicts « il a toujours été là ».</para>
/// </summary>
public class StudentGroupChangeTests
{
    private static readonly DateOnly P1Start = new(2025, 10, 1);
    private static readonly DateOnly P1End   = new(2025, 10, 31);
    private static readonly DateOnly P2Start = new(2025, 11, 1);
    private static readonly DateOnly P2End   = new(2025, 11, 30);
    private static readonly DateOnly Enrolled = new(2025, 9, 1);

    private const int SourceGroupId = 10;
    private const int TargetGroupId = 20;
    private const int SourceCohortId = 101;
    private const int TargetCohortId = 102;

    private static StudentGroupRelocator Relocator(ApplicationDbContext db) =>
        new(db, new AffectationTollReader(db), new CohortMemberScheduler(db));

    private static Task<Result<GroupChangeReport>> ChangeAsync(
        ApplicationDbContext db, Guid registrationId, int targetGroupId = TargetGroupId) =>
        new ChangeStudentGroupCommandHandler(db, Relocator(db), db.AdminAuthorizer())
            .Handle(new ChangeStudentGroupCommand(registrationId, targetGroupId), default);

    private static Task<Result<GroupSwapReport>> SwapAsync(
        ApplicationDbContext db, Guid first, Guid second) =>
        new SwapStudentGroupsCommandHandler(db, Relocator(db), db.AdminAuthorizer())
            .Handle(new SwapStudentGroupsCommand(first, second), default);

    private sealed record Fixture(
        Registration Mover,
        Registration Sitting,
        CohortSlotAssignment SourceCell,
        CohortSlotAssignment TargetCell);

    /// <summary>
    /// Two rosters of one promotion doing one stage in different services, each with a member, each
    /// cell published — the ordinary state of a répartition somebody now wants corrected.
    /// </summary>
    private static async Task<Fixture> SeedTwoRostersAsync(
        ApplicationDbContext db, bool publishTarget = true, bool moverStarted = false)
    {
        var stage = db.SeedCatalog();
        var cardio = db.SeedService(1, "Cardiologie");
        var pneumo = db.SeedService(2, "Pneumologie");

        var source = db.SeedGroup(SourceGroupId, 1);
        var target = db.SeedGroup(TargetGroupId, 2);
        var sourceCohort = db.SeedCohortFor(stage, source, SourceCohortId);
        var targetCohort = db.SeedCohortFor(stage, target, TargetCohortId);

        var slot = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var sourceCell = db.SeedSlotAssignment(1001, sourceCohort, slot, cardio);
        var targetCell = db.SeedSlotAssignment(1002, targetCohort, slot, pneumo);

        var mover = db.SeedRegistration("Sara", "Bennani", source);
        var moverAssignment = db.SeedAssignment(mover, sourceCohort, Enrolled);
        var moverPeriod = db.SeedPeriod(moverAssignment, cardio, P1Start, P1End, started: moverStarted);
        db.SeedCoverage(moverPeriod, sourceCell);
        if (moverStarted) moverAssignment.Start();

        var sitting = db.SeedRegistration("Ali", "Amrani", target);
        var sittingAssignment = db.SeedAssignment(sitting, targetCohort, Enrolled);
        if (publishTarget)
        {
            var period = db.SeedPeriod(sittingAssignment, pneumo, P1Start, P1End, started: false);
            db.SeedCoverage(period, targetCell);
        }

        await db.SaveChangesAsync();
        return new Fixture(mover, sitting, sourceCell, targetCell);
    }

    private static Task<InternshipAssignment> AssignmentOfAsync(
        ApplicationDbContext db, Registration registration) =>
        db.InternshipAssignments
            .Include(a => a.MembershipHistory)
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.SlotCoverage)
            .FirstAsync(a => a.RegistrationId == registration.Id);

    [Fact]
    public async Task The_student_lands_in_the_target_cohorte_and_holds_its_service()
    {
        await using var db = TestHarness.NewContext("group-change-lands");
        var fixture = await SeedTwoRostersAsync(db);

        var result = await ChangeAsync(db, fixture.Mover.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.FromGroupLabel.Should().Be("G1");
        result.Value.ToGroupLabel.Should().Be("G2");
        result.Value.AffectationsMoved.Should().Be(1);

        fixture.Mover.AcademicGroupId.Should().Be(TargetGroupId);

        var assignment = await AssignmentOfAsync(db, fixture.Mover);
        assignment.CurrentCohortId.Should().Be(TargetCohortId);

        var period = assignment.ServicePeriods.Should().ContainSingle().Subject;
        period.ServiceId.Should().Be(2, "he now stands where his new cohorte stands");
        period.CohortSlotAssignmentId.Should().Be(fixture.TargetCell.Id);
        period.SlotCoverage.Should().ContainSingle()
            .Which.CohortSlotAssignmentId.Should().Be(fixture.TargetCell.Id);
    }

    /// <summary>
    /// The whole point of the act: afterwards nothing on the student's side says he was ever anywhere
    /// else. One membership row, still open, still dated the day he was enrolled — and not one domain
    /// event, which is what would have written the <c>GroupTransfer</c> line on his dossier.
    /// </summary>
    [Fact]
    public async Task The_move_leaves_no_trace_on_the_student_file()
    {
        await using var db = TestHarness.NewContext("group-change-silent");
        var fixture = await SeedTwoRostersAsync(db);

        await ChangeAsync(db, fixture.Mover.Id);

        var assignment = await AssignmentOfAsync(db, fixture.Mover);

        var membership = assignment.MembershipHistory.Should().ContainSingle(
            "a correction rewrites the open row; closing it and opening another is what a transfer does")
            .Subject;
        membership.CohortId.Should().Be(TargetCohortId);
        membership.EndDate.Should().BeNull();
        membership.StartDate.Should().Be(Enrolled, "he reads as having been there from the start");
        membership.TransferReason.Should().BeNull();

        fixture.Mover.DomainEvents.Should().BeEmpty(
            "StudentGroupTransferredDomainEvent is what writes the HistoryType.GroupTransfer row");
        assignment.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    /// The control for the test above. A transfer of the same student raises the event, so « aucun
    /// événement » is a fact about this act rather than about a fixture that cannot raise one.
    /// </summary>
    [Fact]
    public async Task A_transfer_of_the_same_student_does_raise_the_event_that_writes_history()
    {
        await using var db = TestHarness.NewContext("group-change-control");
        var fixture = await SeedTwoRostersAsync(db);

        fixture.Mover.TransferToGroup(TargetGroupId, "mutation");

        fixture.Mover.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<StudentGroupTransferredDomainEvent>();
    }

    /// <summary>
    /// ⚠ Not forceable, deliberately. The act that destroys marks and attendance is « Dépublier »,
    /// which names what it costs and asks twice; a roster-side button must never become the way round
    /// it.
    /// </summary>
    [Fact]
    public async Task A_started_rotation_refuses_the_change_and_nothing_moves()
    {
        await using var db = TestHarness.NewContext("group-change-underway");
        var fixture = await SeedTwoRostersAsync(db, moverStarted: true);

        var result = await ChangeAsync(db, fixture.Mover.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GroupChange.RotationsUnderway");
        result.Error.Description.Should().Contain("transfert", "the refusal has to name the act that can do this");

        fixture.Mover.AcademicGroupId.Should().Be(SourceGroupId, "a refusal writes nothing");
        (await AssignmentOfAsync(db, fixture.Mover)).CurrentCohortId.Should().Be(SourceCohortId);
    }

    [Fact]
    public async Task A_roster_of_another_promotion_is_refused()
    {
        await using var db = TestHarness.NewContext("group-change-other-promo");
        var fixture = await SeedTwoRostersAsync(db);

        db.Levels.Add(new Level { Id = 77, Label = "5ème année", Year = 5 });
        db.SeedGroup(30, 3, levelId: 77);
        await db.SaveChangesAsync();

        var result = await ChangeAsync(db, fixture.Mover.Id, targetGroupId: 30);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.TargetGroupInAnotherLevel");
    }

    /// <summary>
    /// « Non réparti » carries no cohorte, so the affectations would stay in the source roster's while
    /// the file says the student is nowhere — the exact state « Vider le groupe » refuses.
    /// </summary>
    [Fact]
    public async Task The_unassigned_bucket_is_refused_as_a_destination()
    {
        await using var db = TestHarness.NewContext("group-change-bucket");
        var fixture = await SeedTwoRostersAsync(db);

        db.SeedUnassignedBucket(40);
        await db.SaveChangesAsync();

        var result = await ChangeAsync(db, fixture.Mover.Id, targetGroupId: 40);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GroupChange.TargetIsUnassignedRoster");
        result.Error.Description.Should().Contain("Vider le groupe");
    }

    /// <summary>
    /// A cohorte nobody published gives its members no période, so the student moved into it gets none
    /// either — materialising the cells would hand him a rotation his classmates do not have.
    /// </summary>
    [Fact]
    public async Task An_unpublished_target_cohorte_gives_him_no_periode()
    {
        await using var db = TestHarness.NewContext("group-change-unpublished");
        var fixture = await SeedTwoRostersAsync(db, publishTarget: false);

        var result = await ChangeAsync(db, fixture.Mover.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsReplaced.Should().Be(1);
        result.Value.PeriodsCreated.Should().Be(0);

        (await AssignmentOfAsync(db, fixture.Mover)).ServicePeriods.Should().BeEmpty();
    }

    /// <summary>
    /// ⚠ A <see cref="StageRotationMode.SingleService"/> run is <b>one</b> période over its whole span,
    /// which is what the cohorte's own members hold. Built one per cell the moved student would be
    /// asked for one mark per column and would average differently from every classmate.
    /// </summary>
    [Fact]
    public async Task A_single_service_run_becomes_one_periode_covering_every_cell()
    {
        await using var db = TestHarness.NewContext("group-change-single-service");

        var stage = db.SeedCatalog();
        stage.RotationMode = StageRotationMode.SingleService;
        var cardio = db.SeedService(1, "Cardiologie");
        var pneumo = db.SeedService(2, "Pneumologie");

        var source = db.SeedGroup(SourceGroupId, 1);
        var target = db.SeedGroup(TargetGroupId, 2);
        var sourceCohort = db.SeedCohortFor(stage, source, SourceCohortId);
        var targetCohort = db.SeedCohortFor(stage, target, TargetCohortId);

        var p1 = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, 101, 2, P2Start, P2End);
        var sourceCell = db.SeedSlotAssignment(1001, sourceCohort, p1, cardio);
        var lead     = db.SeedSlotAssignment(1002, targetCohort, p1, pneumo);
        var trailing = db.SeedSlotAssignment(1003, targetCohort, p2, pneumo);

        var mover = db.SeedRegistration("Sara", "Bennani", source);
        var moverAssignment = db.SeedAssignment(mover, sourceCohort, Enrolled);
        db.SeedCoverage(db.SeedPeriod(moverAssignment, cardio, P1Start, P1End, started: false), sourceCell);

        var sitting = db.SeedRegistration("Ali", "Amrani", target);
        var sittingAssignment = db.SeedAssignment(sitting, targetCohort, Enrolled);
        var stay = db.SeedPeriod(sittingAssignment, pneumo, P1Start, P2End, started: false);
        db.SeedCoverage(stay, lead);
        db.SeedCoverage(stay, trailing, leadCell: false);

        await db.SaveChangesAsync();

        var result = await ChangeAsync(db, mover.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsCreated.Should().Be(1, "the run is one stay, exactly as it was published");

        var period = (await AssignmentOfAsync(db, mover)).ServicePeriods.Should().ContainSingle().Subject;
        period.StartDate.Should().Be(P1Start);
        period.EndDate.Should().Be(P2End);
        period.CohortSlotAssignmentId.Should().Be(lead.Id);
        period.SlotCoverage.Select(c => c.CohortSlotAssignmentId)
            .Should().BeEquivalentTo([lead.Id, trailing.Id],
                "the trailing cell of a run has no foreign key naming it — only the coverage row");
    }

    /// <summary>
    /// A revalidation, a délocalisation or an imported période hangs off no cell: it came from no
    /// répartition, cannot be reproduced by one, and so travels with the student untouched.
    /// </summary>
    [Fact]
    public async Task An_ad_hoc_periode_travels_with_the_student_untouched()
    {
        await using var db = TestHarness.NewContext("group-change-adhoc");
        var fixture = await SeedTwoRostersAsync(db);

        var assignment = await AssignmentOfAsync(db, fixture.Mover);
        var external = db.SeedService(9, "Hôpital militaire");
        db.SeedPeriod(assignment, external, new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 20),
            started: false);
        await db.SaveChangesAsync();

        var result = await ChangeAsync(db, fixture.Mover.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.AdHocPeriodsKept.Should().Be(1);

        var after = await AssignmentOfAsync(db, fixture.Mover);
        after.ServicePeriods.Should().Contain(p => p.ServiceId == 9 && p.CohortSlotAssignmentId == null);
    }

    [Fact]
    public async Task A_roster_that_does_not_run_the_stage_is_refused_rather_than_losing_the_affectation()
    {
        await using var db = TestHarness.NewContext("group-change-missing-stage");
        var stage = db.SeedCatalog();
        var cardio = db.SeedService(1, "Cardiologie");

        var source = db.SeedGroup(SourceGroupId, 1);
        db.SeedGroup(TargetGroupId, 2);
        var sourceCohort = db.SeedCohortFor(stage, source, SourceCohortId);

        var mover = db.SeedRegistration("Sara", "Bennani", source);
        db.SeedAssignment(mover, sourceCohort, Enrolled);
        await db.SaveChangesAsync();

        var result = await ChangeAsync(db, mover.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GroupChange.TargetRosterMissingStage");
        mover.AcademicGroupId.Should().Be(SourceGroupId);
    }

    [Fact]
    public async Task The_target_roster_gives_him_a_cohorte_he_did_not_have()
    {
        await using var db = TestHarness.NewContext("group-change-extra-cohorte");
        var fixture = await SeedTwoRostersAsync(db);

        // A second stage the target roster runs and the source roster does not.
        var second = new Stage { Id = 2, Name = "Pneumologie", LevelId = TestHarness.LevelId, Coefficient = 1 };
        db.Stages.Add(second);
        db.SeedCohortFor(second, fixture.TargetCell.Cohort.AcademicGroup, cohortId: 103);
        await db.SaveChangesAsync();

        var result = await ChangeAsync(db, fixture.Mover.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.AffectationsCreated.Should().Be(1);

        var created = await db.InternshipAssignments
            .Include(a => a.MembershipHistory)
            .FirstAsync(a => a.RegistrationId == fixture.Mover.Id && a.CurrentCohortId == 103);

        created.MembershipHistory.Should().ContainSingle()
            .Which.StartDate.Should().Be(Enrolled,
                "a row dated the day of the correction is the trace this act must not leave");
    }

    [Fact]
    public async Task An_echange_exchanges_the_two_rosters()
    {
        await using var db = TestHarness.NewContext("group-swap");
        var fixture = await SeedTwoRostersAsync(db);

        var result = await SwapAsync(db, fixture.Mover.Id, fixture.Sitting.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.First.ToGroupLabel.Should().Be("G2");
        result.Value.Second.ToGroupLabel.Should().Be("G1");

        fixture.Mover.AcademicGroupId.Should().Be(TargetGroupId);
        fixture.Sitting.AcademicGroupId.Should().Be(SourceGroupId);

        (await AssignmentOfAsync(db, fixture.Mover)).CurrentCohortId.Should().Be(TargetCohortId);
        (await AssignmentOfAsync(db, fixture.Sitting)).CurrentCohortId.Should().Be(SourceCohortId);
    }

    /// <summary>
    /// ⚠ An échange that half happened is two rosters of the wrong size and nothing saying why. The
    /// second student's refusal has to leave the first exactly where he was.
    /// </summary>
    [Fact]
    public async Task An_echange_refused_on_one_side_moves_neither()
    {
        await using var db = TestHarness.NewContext("group-swap-refused");
        var fixture = await SeedTwoRostersAsync(db, moverStarted: true);

        var result = await SwapAsync(db, fixture.Sitting.Id, fixture.Mover.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GroupChange.RotationsUnderway");

        var sitting = await db.Registrations.AsNoTracking()
            .FirstAsync(r => r.Id == fixture.Sitting.Id);
        sitting.AcademicGroupId.Should().Be(TargetGroupId,
            "nothing was saved, so the half that succeeded in memory never reached the store");
    }

    [Fact]
    public async Task An_echange_needs_two_different_students_in_two_different_rosters()
    {
        await using var db = TestHarness.NewContext("group-swap-guards");
        var fixture = await SeedTwoRostersAsync(db);

        var withSelf = await SwapAsync(db, fixture.Mover.Id, fixture.Mover.Id);
        withSelf.Error.Code.Should().Be("GroupChange.CannotSwapWithSelf");

        var roommate = db.SeedRegistration(
            "Nadia", "Cherkaoui", await db.AcademicGroups.FirstAsync(g => g.Id == SourceGroupId));
        await db.SaveChangesAsync();

        var sameGroup = await SwapAsync(db, fixture.Mover.Id, roommate.Id);
        sameGroup.Error.Code.Should().Be("GroupChange.SwapWithinOneGroup");
    }

    [Fact]
    public async Task A_registration_in_no_roster_is_told_to_join_one()
    {
        await using var db = TestHarness.NewContext("group-change-no-roster");
        await SeedTwoRostersAsync(db);

        var unplaced = db.SeedRegistration("Omar", "Idrissi");
        await db.SaveChangesAsync();

        var result = await ChangeAsync(db, unplaced.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GroupChange.NotInAGroup");
        result.Error.Description.Should().Contain("Affecter à un groupe");
    }

    [Fact]
    public async Task The_affectation_is_left_a_plan_and_carries_no_score()
    {
        await using var db = TestHarness.NewContext("group-change-status");
        var fixture = await SeedTwoRostersAsync(db);

        await ChangeAsync(db, fixture.Mover.Id);

        var assignment = await AssignmentOfAsync(db, fixture.Mover);
        assignment.Status.Should().Be(InternshipStatus.Planned);
        assignment.FinalScore.Should().BeNull();
    }
}
