using FluentAssertions;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The opt-in forced hand-off (Reschedule = true). A student moved while his stage is already running
// has the in-flight rotation cut into read-only history at the origin service, and the remaining
// windows re-created against the target group's grid so the NEW chef supervises and evaluates.
// It refuses outright when the target group has no plan for a period being moved.
public class MidStageRerouteTests
{
    private const int OriginCohortId  = 1;
    private const int TargetCohortId  = 2;
    private const int OriginServiceId = 10;
    private const int TargetServiceId = 20;

    private static readonly DateOnly P1Start = new(2026, 3, 1);
    private static readonly DateOnly P1End   = new(2026, 3, 31);
    private static readonly DateOnly P2Start = new(2026, 4, 1);
    private static readonly DateOnly P2End   = new(2026, 4, 30);
    private static readonly DateOnly Moved   = new(2026, 3, 15);

    /// <summary>Both groups planned across the same two periods, each in its own service.</summary>
    private static async Task SeedGridAsync(ApplicationDbContext db, bool targetHasPeriod2 = true)
    {
        var stage = db.SeedCatalog();
        var originService = db.SeedService(OriginServiceId, "Cardiologie");
        var targetService = db.SeedService(TargetServiceId, "Réanimation");

        var origin = db.SeedCohort(stage, OriginCohortId, "Groupe 1");
        var target = db.SeedCohort(stage, TargetCohortId, "Groupe 2");

        var slot1 = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var slot2 = db.SeedSlot(stage, 200, 2, P2Start, P2End);

        db.SeedSlotAssignment(1, origin, slot1, originService);
        db.SeedSlotAssignment(2, origin, slot2, originService);
        db.SeedSlotAssignment(3, target, slot1, targetService);
        if (targetHasPeriod2)
            db.SeedSlotAssignment(4, target, slot2, targetService);

        await db.SaveChangesAsync();
    }

    /// <summary>A student mid-stage: period 1 running at the origin, period 2 still planned.</summary>
    private static InternshipAssignment InFlight(ApplicationDbContext db)
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = TargetCohortId };
        Add(1, P1Start, P1End, started: true);
        Add(2, P2Start, P2End, started: false);
        return assignment;

        void Add(int slotAssignmentId, DateOnly start, DateOnly end, bool started) =>
            assignment.ServicePeriods.Add(new ServicePeriod
            {
                Id = Guid.NewGuid(), InternshipAssignmentId = assignment.Id,
                ServiceId = OriginServiceId, CohortSlotAssignmentId = slotAssignmentId,
                StartDate = start, EndDate = end, IsStarted = started,
                CohortSlotAssignment = db.CohortSlotAssignments.Local.Single(x => x.Id == slotAssignmentId),
            });
    }

    [Fact]
    public async Task The_running_rotation_is_cut_into_read_only_history_at_the_origin()
    {
        await using var db = TestHarness.NewContext("reroute-cut");
        await SeedGridAsync(db);
        var assignment = InFlight(db);

        var result = await new MidStageTransferRescheduler(db)
            .RerouteAsync(assignment, TargetCohortId, Moved, default);

        result.IsSuccess.Should().BeTrue();
        var cut = assignment.ServicePeriods.Single(p => p.IsInterrupted);
        cut.ServiceId.Should().Be(OriginServiceId);
        cut.EndDate.Should().Be(Moved, "the rotation stops the day the student leaves");
    }

    [Fact]
    public async Task The_remaining_window_becomes_a_live_rotation_at_the_target_service()
    {
        await using var db = TestHarness.NewContext("reroute-landing");
        await SeedGridAsync(db);
        var assignment = InFlight(db);

        await new MidStageTransferRescheduler(db).RerouteAsync(assignment, TargetCohortId, Moved, default);

        var landed = assignment.ServicePeriods
            .Single(p => p.ServiceId == TargetServiceId && p.IsStarted && !p.IsInterrupted);
        landed.EndDate.Should().Be(P1End, "the student finishes the period with his new group");
        landed.CohortSlotAssignmentId.Should().Be(3);
    }

    [Fact]
    public async Task Future_rotations_are_rehomed_onto_the_target_grid_and_stay_inactive()
    {
        await using var db = TestHarness.NewContext("reroute-future");
        await SeedGridAsync(db);
        var assignment = InFlight(db);

        await new MidStageTransferRescheduler(db).RerouteAsync(assignment, TargetCohortId, Moved, default);

        var future = assignment.ServicePeriods.Single(p => p.StartDate == P2Start);
        future.ServiceId.Should().Be(TargetServiceId);
        future.CohortSlotAssignmentId.Should().Be(4);
        future.IsStarted.Should().BeFalse("it starts in the normal flow, with the rest of the group");
    }

    [Fact]
    public async Task A_completed_rotation_is_never_touched()
    {
        await using var db = TestHarness.NewContext("reroute-history");
        await SeedGridAsync(db);
        var assignment = InFlight(db);
        var done = assignment.ServicePeriods.Single(p => p.StartDate == P1Start);
        done.IsComplete = true;
        // Period 2 is still planned, so there is nothing in flight — the reroute is a no-op.

        var result = await new MidStageTransferRescheduler(db)
            .RerouteAsync(assignment, TargetCohortId, Moved, default);

        result.IsSuccess.Should().BeTrue();
        done.ServiceId.Should().Be(OriginServiceId, "closed rotations keep their history");
        done.IsInterrupted.Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_in_flight_means_nothing_to_reroute()
    {
        await using var db = TestHarness.NewContext("reroute-noop");
        await SeedGridAsync(db);
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = TargetCohortId };
        assignment.ServicePeriods.Add(new ServicePeriod
        {
            Id = Guid.NewGuid(), InternshipAssignmentId = assignment.Id,
            ServiceId = OriginServiceId, CohortSlotAssignmentId = 1,
            StartDate = P1Start, EndDate = P1End, IsStarted = false,
            CohortSlotAssignment = db.CohortSlotAssignments.Local.Single(x => x.Id == 1),
        });

        var result = await new MidStageTransferRescheduler(db)
            .RerouteAsync(assignment, TargetCohortId, Moved, default);

        result.IsSuccess.Should().BeTrue();
        assignment.ServicePeriods.Should().ContainSingle()
            .Which.ServiceId.Should().Be(OriginServiceId, "the plain transfer path covers this case");
    }

    [Fact]
    public async Task A_target_group_missing_a_period_of_the_plan_is_refused_by_number()
    {
        await using var db = TestHarness.NewContext("reroute-gap");
        await SeedGridAsync(db, targetHasPeriod2: false);
        var assignment = InFlight(db);

        var result = await new MidStageTransferRescheduler(db)
            .RerouteAsync(assignment, TargetCohortId, Moved, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.TargetScheduleMissingPeriods(TargetCohortId, [2]));
        result.Error.Description.Should().Contain("2");
    }

    [Fact]
    public async Task A_refused_reroute_leaves_the_rotation_exactly_as_it_was()
    {
        await using var db = TestHarness.NewContext("reroute-gap-intact");
        await SeedGridAsync(db, targetHasPeriod2: false);
        var assignment = InFlight(db);

        await new MidStageTransferRescheduler(db).RerouteAsync(assignment, TargetCohortId, Moved, default);

        assignment.ServicePeriods.Should().HaveCount(2);
        assignment.ServicePeriods.Should().OnlyContain(p => p.ServiceId == OriginServiceId);
        assignment.ServicePeriods.Should().NotContain(p => p.IsInterrupted);
    }

    [Fact]
    public async Task New_rotations_leave_their_keys_for_the_store_to_generate()
    {
        await using var db = TestHarness.NewContext("reroute-keys");
        await SeedGridAsync(db);
        var assignment = InFlight(db);

        await new MidStageTransferRescheduler(db).RerouteAsync(assignment, TargetCohortId, Moved, default);

        assignment.ServicePeriods
            .Where(p => p.ServiceId == TargetServiceId)
            .Should().OnlyContain(p => p.Id == Guid.Empty,
                "a pre-set store-generated key makes EF UPDATE a row that does not exist");
    }

    // ─── Regressions: dates and ad-hoc rotations ──────────────────────────────

    // The "missing slots" guard admitted a slot-less period (a null cell has no period number to
    // report, so it fell out of the list), and the loop then dereferenced that cell and threw.
    [Fact]
    public async Task An_ad_hoc_rotation_is_refused_instead_of_throwing()
    {
        await using var db = TestHarness.NewContext("reroute-adhoc");
        await SeedGridAsync(db);
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = TargetCohortId };
        assignment.ServicePeriods.Add(new ServicePeriod
        {
            Id = Guid.NewGuid(), InternshipAssignmentId = assignment.Id,
            ServiceId = OriginServiceId, CohortSlotAssignmentId = null,   // délocalisation / rattrapage
            StartDate = P1Start, EndDate = P1End, IsStarted = true,
        });

        var result = await new MidStageTransferRescheduler(db)
            .RerouteAsync(assignment, TargetCohortId, Moved, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.CannotRerouteAdHocPeriod);
    }

    // A transfer dated before the target slot opens used to start the new rotation on the transfer
    // date — i.e. before the window it belongs to existed.
    [Fact]
    public async Task A_transfer_before_the_target_slot_opens_starts_the_rotation_when_the_slot_does()
    {
        await using var db = TestHarness.NewContext("reroute-early");
        await SeedGridAsync(db);
        var assignment = InFlight(db);
        var early = P1Start.AddDays(-20);

        await new MidStageTransferRescheduler(db).RerouteAsync(assignment, TargetCohortId, early, default);

        var landed = assignment.ServicePeriods
            .Single(p => p.ServiceId == TargetServiceId && p.IsStarted && !p.IsInterrupted);
        landed.StartDate.Should().Be(P1Start, "a rotation cannot begin before its slot opens");
        landed.StartDate.Should().BeOnOrBefore(landed.EndDate);
    }

    [Fact]
    public async Task A_backdated_transfer_never_ends_a_rotation_before_it_began()
    {
        await using var db = TestHarness.NewContext("reroute-backdated");
        await SeedGridAsync(db);
        var assignment = InFlight(db);
        var early = P1Start.AddDays(-20);

        await new MidStageTransferRescheduler(db).RerouteAsync(assignment, TargetCohortId, early, default);

        var cut = assignment.ServicePeriods.Single(p => p.IsInterrupted);
        cut.EndDate.Should().Be(P1Start, "the cut is clamped to the day the rotation started");
        cut.EndDate.Should().BeOnOrAfter(cut.StartDate);
    }
}
