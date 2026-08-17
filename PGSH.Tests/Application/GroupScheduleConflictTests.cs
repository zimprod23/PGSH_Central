using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The rule that actually prevents double-booking: <b>no academic group may be in two services at
/// the same time</b>, checked where a group is really placed rather than where a date column is
/// merely declared.
///
/// The layout it has to permit is the faculty's own (<c>example_stage_assignement/Med3.png</c>): a
/// promotion split into partitions, with Médecine P1 and Chirurgie P1 running the <i>same</i> dates —
/// A in one, B in the other. Halving the load on every service is the whole reason partitions exist.
/// The layout it has to refuse is the same group in both.
/// </summary>
public class GroupScheduleConflictTests
{
    private const int ChirurgieId = 2;

    private static readonly DateOnly P1Start = new(2025, 11, 3);
    private static readonly DateOnly P1End   = new(2025, 12, 31);
    private static readonly DateOnly P2Start = new(2026, 1, 1);
    private static readonly DateOnly P2End   = new(2026, 2, 28);

    private static SetCohortSlotAssignmentCommandHandler Handler(ApplicationDbContext db) =>
        new(db, new GroupScheduleConflictGuard(db));

    /// <summary>
    /// Two stages sharing one period axis — the shape under test. Médecine and Chirurgie each own a
    /// P1 and a P2 over identical dates.
    /// </summary>
    private static (Stage Medecine, Stage Chirurgie) SeedTwoStagesOnOneAxis(ApplicationDbContext db)
    {
        var medecine  = db.SeedCatalog();
        var chirurgie = db.SeedStage(ChirurgieId, "Chirurgie");

        db.SeedService(10, "Médecine A");
        db.SeedService(20, "Chirurgie A");

        db.SeedSlot(medecine,  1, 1, P1Start, P1End);
        db.SeedSlot(medecine,  2, 2, P2Start, P2End);
        db.SeedSlot(chirurgie, 3, 1, P1Start, P1End);
        db.SeedSlot(chirurgie, 4, 2, P2Start, P2End);

        return (medecine, chirurgie);
    }

    [Fact]
    public async Task Two_partitions_may_occupy_the_same_dates_in_different_stages()
    {
        // Group 1 (partition A) → Médecine P1; group 2 (partition B) → Chirurgie P1, same window.
        await using var db = TestHarness.NewContext("group-conflict-partitions");
        var (medecine, chirurgie) = SeedTwoStagesOnOneAxis(db);

        var groupA = db.SeedGroup(1, 1, rotationGroup: "A");
        var groupB = db.SeedGroup(2, 2, rotationGroup: "B");
        var medA   = db.SeedCohortFor(medecine, groupA, 101);
        var chirB  = db.SeedCohortFor(chirurgie, groupB, 202);
        await db.SaveChangesAsync();

        var first  = await Handler(db).Handle(new SetCohortSlotAssignmentCommand(medA.Id, 1, 10), default);
        var second = await Handler(db).Handle(new SetCohortSlotAssignmentCommand(chirB.Id, 3, 20), default);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue("partitions A and B share no student");
        (await db.CohortSlotAssignments.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task One_group_may_not_occupy_two_stages_over_the_same_dates()
    {
        await using var db = TestHarness.NewContext("group-conflict-same-group");
        var (medecine, chirurgie) = SeedTwoStagesOnOneAxis(db);

        var group = db.SeedGroup(1, 7, rotationGroup: "A");
        var med   = db.SeedCohortFor(medecine, group, 101);
        var chir  = db.SeedCohortFor(chirurgie, group, 201);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new SetCohortSlotAssignmentCommand(med.Id, 1, 10), default);
        var clash = await Handler(db).Handle(new SetCohortSlotAssignmentCommand(chir.Id, 3, 20), default);

        clash.IsFailure.Should().BeTrue();
        clash.Error.Code.Should().Be("Schedule.GroupAlreadyPlaced");
        // The promotion is named as well as the stage: a legacy group row is shared by every level
        // that used its number, so the stage it is busy in is often another year of study entirely.
        clash.Error.Description.Should()
            .Contain("groupe 7").And.Contain("Cardiologie").And.Contain("3ème année");
        (await db.CohortSlotAssignments.CountAsync()).Should().Be(1, "the clashing cell is not written");
    }

    [Fact]
    public async Task The_same_group_may_take_the_other_stage_in_a_later_period()
    {
        // A→Médecine P1, then A→Chirurgie P2: the windows do not overlap, so this is the normal
        // way a partition moves from one stage to the next.
        await using var db = TestHarness.NewContext("group-conflict-sequential");
        var (medecine, chirurgie) = SeedTwoStagesOnOneAxis(db);

        var group = db.SeedGroup(1, 1, rotationGroup: "A");
        var med   = db.SeedCohortFor(medecine, group, 101);
        var chir  = db.SeedCohortFor(chirurgie, group, 201);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new SetCohortSlotAssignmentCommand(med.Id, 1, 10), default);
        var later = await Handler(db).Handle(new SetCohortSlotAssignmentCommand(chir.Id, 4, 20), default);

        later.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Changing_the_service_of_a_cell_the_group_already_holds_is_not_a_conflict()
    {
        await using var db = TestHarness.NewContext("group-conflict-same-cell");
        var (medecine, _) = SeedTwoStagesOnOneAxis(db);
        db.SeedService(11, "Néphrologie");

        var group = db.SeedGroup(1, 1);
        var med   = db.SeedCohortFor(medecine, group, 101);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new SetCohortSlotAssignmentCommand(med.Id, 1, 10), default);
        var moved = await Handler(db).Handle(new SetCohortSlotAssignmentCommand(med.Id, 1, 11), default);

        moved.IsSuccess.Should().BeTrue();
        (await db.CohortSlotAssignments.SingleAsync()).ServiceId.Should().Be(11);
    }

    [Fact]
    public async Task Arranging_a_stage_skips_groups_already_placed_over_the_same_dates_and_counts_them()
    {
        // The realistic mistake: Médecine is arranged for every partition across the shared axis,
        // then Chirurgie is arranged the same way. Every group is already busy, so nothing is
        // written — and the count says why rather than leaving a silently empty grid.
        await using var db = TestHarness.NewContext("group-conflict-arrange");
        var (medecine, chirurgie) = SeedTwoStagesOnOneAxis(db);

        var medService  = db.Services.Local.First(s => s.Id == 10);
        var chirService = db.Services.Local.First(s => s.Id == 20);
        medecine.AllowedServices.Add(medService);
        chirurgie.AllowedServices.Add(chirService);

        foreach (int number in new[] { 1, 2 })
        {
            var group = db.SeedGroup(number, number, rotationGroup: number == 1 ? "A" : "B");
            db.SeedCohortFor(medecine, group, 100 + number);
            db.SeedCohortFor(chirurgie, group, 200 + number);
        }
        await db.SaveChangesAsync();

        var arranger = db.Arranger();

        var medecineRun = await arranger.ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        medecineRun.IsSuccess.Should().BeTrue();
        medecineRun.Value.Assigned.Should().Be(4, "2 groups × 2 periods");
        medecineRun.Value.GroupConflicts.Should().Be(0);

        var chirurgieRun = await arranger.ArrangeAsync(
            ChirurgieId, TestHarness.CurrentYearId, null, null, null, default);

        chirurgieRun.IsSuccess.Should().BeTrue();
        chirurgieRun.Value.Assigned.Should().Be(0);
        chirurgieRun.Value.GroupConflicts.Should().Be(4, "both groups are busy in both periods");
    }

    [Fact]
    public async Task Arranging_one_partition_per_stage_fills_the_shared_axis_without_conflict()
    {
        // The layout the faculty actually wants: A→Médecine P1-P2, B→Chirurgie P1-P2 over the same
        // dates. Both stages fill every column, and nobody is in two places.
        await using var db = TestHarness.NewContext("group-conflict-arrange-partitioned");
        var (medecine, chirurgie) = SeedTwoStagesOnOneAxis(db);

        medecine.AllowedServices.Add(db.Services.Local.First(s => s.Id == 10));
        chirurgie.AllowedServices.Add(db.Services.Local.First(s => s.Id == 20));

        foreach (int number in new[] { 1, 2 })
        {
            var group = db.SeedGroup(number, number, rotationGroup: number == 1 ? "A" : "B");
            db.SeedCohortFor(medecine, group, 100 + number);
            db.SeedCohortFor(chirurgie, group, 200 + number);
        }
        await db.SaveChangesAsync();

        var arranger = db.Arranger();

        var medecineRun = await arranger.ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, ["A"], null, null, default);
        var chirurgieRun = await arranger.ArrangeAsync(
            ChirurgieId, TestHarness.CurrentYearId, ["B"], null, null, default);

        medecineRun.Value.Assigned.Should().Be(2);
        medecineRun.Value.GroupConflicts.Should().Be(0);
        chirurgieRun.Value.Assigned.Should().Be(2);
        chirurgieRun.Value.GroupConflicts.Should().Be(0, "B never entered Médecine");

        (await db.CohortSlotAssignments.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task A_refused_cell_keeps_the_plan_it_already_had()
    {
        // The destructive shape: Médecine is arranged for both partitions, Chirurgie P1 is then
        // planned for B over the same window, and someone re-runs Médecine across all partitions.
        // The removal of "stale" cells must not run ahead of the refusal — deleting a cohort's cell
        // and then declining to write its replacement loses a plan that was correct.
        await using var db = TestHarness.NewContext("group-conflict-no-data-loss");
        var (medecine, chirurgie) = SeedTwoStagesOnOneAxis(db);

        medecine.AllowedServices.Add(db.Services.Local.First(s => s.Id == 10));
        chirurgie.AllowedServices.Add(db.Services.Local.First(s => s.Id == 20));

        foreach (int number in new[] { 1, 2 })
        {
            var group = db.SeedGroup(number, number, rotationGroup: number == 1 ? "A" : "B");
            db.SeedCohortFor(medecine, group, 100 + number);
            db.SeedCohortFor(chirurgie, group, 200 + number);
        }
        await db.SaveChangesAsync();

        var arranger = db.Arranger();

        await arranger.ArrangeAsync(TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);
        (await db.CohortSlotAssignments.CountAsync()).Should().Be(4);

        // Group 2 also sits in Chirurgie P1, which shares Médecine P1's window. Seeded directly
        // rather than through the command, because the guard would now refuse to create it — this
        // is the state the database is already in, from before the rule existed.
        db.SeedSlotAssignment(900, db.Cohorts.Local.First(c => c.Id == 202),
            db.StageSlots.Local.First(s => s.Id == 3), db.Services.Local.First(s => s.Id == 20));
        await db.SaveChangesAsync();

        var rerun = await arranger.ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        rerun.Value.GroupConflicts.Should().Be(1, "group 2 is busy in Chirurgie over Médecine P1");

        // 4 Médecine cells + 1 Chirurgie cell. Before the fix, group 2's Médecine P1 cell was
        // deleted and never replaced, leaving 4.
        (await db.CohortSlotAssignments.CountAsync()).Should().Be(5);
        (await db.CohortSlotAssignments
            .AnyAsync(a => a.CohortId == 102 && a.StageSlotId == 1))
            .Should().BeTrue("the refused cell keeps the service it already had");
    }

    [Fact]
    public async Task Moving_a_period_onto_another_stages_window_is_refused_when_a_group_is_in_both()
    {
        // Double-booking need not go through a cell: dragging a period's dates does it to every
        // group already in it. SlotOverlapGuard only compares periods of the same stage, so the
        // update path has to ask the group guard directly.
        await using var db = TestHarness.NewContext("group-conflict-slot-move");
        var medecine  = db.SeedCatalog();
        var chirurgie = db.SeedStage(ChirurgieId, "Chirurgie");
        db.SeedService(10, "Médecine A");
        db.SeedService(20, "Chirurgie A");
        db.SeedSlot(medecine, 1, 1, P1Start, P1End);

        // Chirurgie's only period sits well clear of Médecine P1 — otherwise the per-stage overlap
        // guard fires first and we would never reach the group check.
        var chirLate = db.SeedSlot(chirurgie, 5, 1, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31));

        var group = db.SeedGroup(1, 4, rotationGroup: "A");
        var med   = db.SeedCohortFor(medecine, group, 101);
        var chir  = db.SeedCohortFor(chirurgie, group, 201);
        await db.SaveChangesAsync();

        var handler = Handler(db);
        await handler.Handle(new SetCohortSlotAssignmentCommand(med.Id, 1, 10), default);
        await handler.Handle(new SetCohortSlotAssignmentCommand(chir.Id, chirLate.Id, 20), default);

        var moved = await new UpdateStageSlotCommandHandler(
                db, new SlotOverlapGuard(db), new GroupScheduleConflictGuard(db))
            .Handle(new UpdateStageSlotCommand(chirLate.Id, ChirurgieId, null, P1Start, P1End), default);

        moved.IsFailure.Should().BeTrue();
        moved.Error.Code.Should().Be("Schedule.GroupAlreadyPlaced");
        (await db.StageSlots.FirstAsync(s => s.Id == chirLate.Id)).StartDate
            .Should().Be(new DateOnly(2026, 4, 1), "the move is refused, not partially applied");
    }

    [Fact]
    public async Task Moving_a_period_that_holds_nobody_is_still_allowed()
    {
        await using var db = TestHarness.NewContext("group-conflict-slot-move-empty");
        var medecine  = db.SeedCatalog();
        var chirurgie = db.SeedStage(ChirurgieId, "Chirurgie");
        db.SeedSlot(medecine, 1, 1, P1Start, P1End);
        var chirLate = db.SeedSlot(chirurgie, 5, 1, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31));
        await db.SaveChangesAsync();

        var moved = await new UpdateStageSlotCommandHandler(
                db, new SlotOverlapGuard(db), new GroupScheduleConflictGuard(db))
            .Handle(new UpdateStageSlotCommand(chirLate.Id, ChirurgieId, null, P1Start, P1End), default);

        moved.IsSuccess.Should().BeTrue("an empty period places nobody");
    }

    [Fact]
    public async Task Re_arranging_the_same_stage_does_not_conflict_with_its_own_cells()
    {
        // The slots being rewritten are excluded from the occupancy snapshot; otherwise a second
        // run would see the cells it is about to replace and refuse every one of them.
        await using var db = TestHarness.NewContext("group-conflict-rearrange");
        var (medecine, _) = SeedTwoStagesOnOneAxis(db);
        medecine.AllowedServices.Add(db.Services.Local.First(s => s.Id == 10));

        var group = db.SeedGroup(1, 1, rotationGroup: "A");
        db.SeedCohortFor(medecine, group, 101);
        await db.SaveChangesAsync();

        var arranger = db.Arranger();

        await arranger.ArrangeAsync(TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);
        var again = await arranger.ArrangeAsync(TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        again.IsSuccess.Should().BeTrue();
        again.Value.Assigned.Should().Be(2);
        again.Value.GroupConflicts.Should().Be(0);
    }
}
