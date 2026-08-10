using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.RotationCycle;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The database half: the block's stages must belong to the level, the axis is written once for all of
/// them, and it is never rewritten under a published plan.
/// </summary>
public class RotationCycleHandlerTests
{
    private const int ChirurgieId = 2;

    private static ApplyRotationCycleCommandHandler ApplyHandler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db), new RotationCycleContext(db));

    private static PreviewRotationCycleQueryHandler PreviewHandler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db), new RotationCycleContext(db));

    private static List<DateWindow> Months(int count) =>
        Enumerable.Range(0, count)
            .Select(i =>
            {
                var start = new DateOnly(2025, 10, 1).AddMonths(i);
                return new DateWindow(start, start.AddMonths(1).AddDays(-1));
            })
            .ToList();

    /// <summary>Two stages of the shared level, and a promotion cut into partitions A and B.</summary>
    private static void SeedBlock(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedStage(ChirurgieId, "Chirurgie");
        db.SeedGroup(groupId: 1, groupNumber: 1, rotationGroup: "A");
        db.SeedGroup(groupId: 2, groupNumber: 2, rotationGroup: "B");
    }

    [Fact]
    public async Task Applying_writes_the_same_windows_to_every_stage_of_the_block()
    {
        await using var db = TestHarness.NewContext(nameof(Applying_writes_the_same_windows_to_every_stage_of_the_block));
        SeedBlock(db);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyRotationCycleCommand(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(ChirurgieId, 2)], Months(4)),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.SlotsCreated.Should().Be(8);   // 2 stages × 4 columns

        var slots = await db.StageSlots.ToListAsync();

        // The whole point: P1 of Médecine and P1 of Chirurgie cannot drift, because they are the same
        // dates written once rather than the same dates typed twice.
        foreach (int period in new[] { 1, 2, 3, 4 })
        {
            var windows = slots.Where(s => s.PeriodNumber == period)
                .Select(s => (s.StartDate, s.EndDate))
                .Distinct()
                .ToList();

            windows.Should().ContainSingle($"every stage declares P{period} on one window");
        }
    }

    [Fact]
    public async Task The_returned_matrix_is_the_crossover_the_macro_plan_consumes()
    {
        await using var db = TestHarness.NewContext(nameof(The_returned_matrix_is_the_crossover_the_macro_plan_consumes));
        SeedBlock(db);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyRotationCycleCommand(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(ChirurgieId, 2)], Months(4)),
            default);

        var matrix = result.Value.Matrix;

        matrix.Single(m => m.RotationGroup == "A" && m.StageId == TestHarness.StageId)
            .PeriodNumbers.Should().BeEquivalentTo([1, 2]);
        matrix.Single(m => m.RotationGroup == "B" && m.StageId == ChirurgieId)
            .PeriodNumbers.Should().BeEquivalentTo([1, 2]);
        matrix.Single(m => m.RotationGroup == "A" && m.StageId == ChirurgieId)
            .PeriodNumbers.Should().BeEquivalentTo([3, 4]);
    }

    [Fact]
    public async Task The_preview_writes_nothing_and_matches_what_the_apply_then_does()
    {
        await using var db = TestHarness.NewContext(nameof(The_preview_writes_nothing_and_matches_what_the_apply_then_does));
        SeedBlock(db);
        await db.SaveChangesAsync();

        var preview = await PreviewHandler(db).Handle(
            new PreviewRotationCycleQuery(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(ChirurgieId, 2)], Months(4)),
            default);

        preview.IsSuccess.Should().BeTrue();
        preview.Value.CanApply.Should().BeTrue();
        (await db.StageSlots.CountAsync()).Should().Be(0);

        var applied = await ApplyHandler(db).Handle(
            new ApplyRotationCycleCommand(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(ChirurgieId, 2)], Months(4)),
            default);

        applied.Value.Layout.Matrix.Should().BeEquivalentTo(preview.Value.Layout.Matrix);
    }

    [Fact]
    public async Task A_stage_of_another_level_cannot_join_the_block()
    {
        await using var db = TestHarness.NewContext(nameof(A_stage_of_another_level_cannot_join_the_block));
        SeedBlock(db);
        db.SeedLevel(9, "1ère année Pharmacie", year: 1, PGSH.Domain.Common.Utils.AcademicProgram.Pharmacie);
        db.SeedStage(50, "Officine", levelId: 9);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyRotationCycleCommand(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(50, 2)], Months(4)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.StageNotOfLevel");
    }

    [Fact]
    public async Task Re_applying_replaces_the_axis_rather_than_adding_a_second_one()
    {
        await using var db = TestHarness.NewContext(nameof(Re_applying_replaces_the_axis_rather_than_adding_a_second_one));
        SeedBlock(db);
        await db.SaveChangesAsync();

        var command = new ApplyRotationCycleCommand(
            TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(ChirurgieId, 2)], Months(4));

        await ApplyHandler(db).Handle(command, default);
        var second = await ApplyHandler(db).Handle(command, default);

        // Half-old, half-new columns are exactly the misalignment this feature removes.
        second.Value.SlotsReplaced.Should().Be(8);
        second.Value.SlotsCreated.Should().Be(8);
        (await db.StageSlots.CountAsync()).Should().Be(8);
    }

    [Fact]
    public async Task The_axis_is_never_rewritten_under_a_published_plan()
    {
        await using var db = TestHarness.NewContext(nameof(The_axis_is_never_rewritten_under_a_published_plan));
        var stage = db.SeedCatalog();
        db.SeedStage(ChirurgieId, "Chirurgie");
        var group = db.SeedGroup(groupId: 1, groupNumber: 1, rotationGroup: "A");
        db.SeedGroup(groupId: 2, groupNumber: 2, rotationGroup: "B");

        var service = db.SeedService(3, "Cardiologie");
        var cohort = db.SeedCohortFor(stage, group, cohortId: 30);
        var slot = db.SeedSlot(stage, slotId: 1, periodNumber: 1,
            new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));
        var cell = db.SeedSlotAssignment(id: 1, cohort, slot, service);

        var registration = db.SeedRegistration("Sara", "Bennani", group);
        var assignment = db.SeedAssignment(registration, cohort);
        var period = db.SeedPeriod(assignment, service,
            new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));
        period.CohortSlotAssignmentId = cell.Id;
        period.CohortSlotAssignment = cell;
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyRotationCycleCommand(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(ChirurgieId, 2)], Months(4)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.CannotReplacePublished");
        (await db.StageSlots.CountAsync()).Should().Be(1, "nothing was written");
    }

    [Fact]
    public async Task The_preview_says_up_front_that_a_published_block_cannot_be_re_authored()
    {
        await using var db = TestHarness.NewContext(nameof(The_preview_says_up_front_that_a_published_block_cannot_be_re_authored));
        var stage = db.SeedCatalog();
        db.SeedStage(ChirurgieId, "Chirurgie");
        var group = db.SeedGroup(groupId: 1, groupNumber: 1, rotationGroup: "A");
        db.SeedGroup(groupId: 2, groupNumber: 2, rotationGroup: "B");

        var service = db.SeedService(3, "Cardiologie");
        var cohort = db.SeedCohortFor(stage, group, cohortId: 30);
        var slot = db.SeedSlot(stage, slotId: 1, periodNumber: 1,
            new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));
        var cell = db.SeedSlotAssignment(id: 1, cohort, slot, service);

        var registration = db.SeedRegistration("Sara", "Bennani", group);
        var assignment = db.SeedAssignment(registration, cohort);
        var period = db.SeedPeriod(assignment, service,
            new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));
        period.CohortSlotAssignmentId = cell.Id;
        period.CohortSlotAssignment = cell;
        await db.SaveChangesAsync();

        var preview = await PreviewHandler(db).Handle(
            new PreviewRotationCycleQuery(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(ChirurgieId, 2)], Months(4)),
            default);

        // Finding out at apply time that the block is frozen is worse than being told before filling
        // in twelve date ranges.
        preview.Value.CanApply.Should().BeFalse();
        preview.Value.PublishedCells.Should().Be(1);
    }

    [Fact]
    public async Task A_stage_listed_twice_is_refused_by_the_handler_and_not_only_by_the_planner()
    {
        await using var db = TestHarness.NewContext(nameof(A_stage_listed_twice_is_refused_by_the_handler_and_not_only_by_the_planner));
        SeedBlock(db);
        await db.SaveChangesAsync();

        // Found against the running API: the context resolves and indexes the stage ids *before* the
        // planner ever sees them, so its DuplicateStage guard was unreachable and a repeated id threw
        // out of ToDictionary as a 500. Testing the planner in isolation could not catch this.
        var result = await ApplyHandler(db).Handle(
            new ApplyRotationCycleCommand(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(TestHarness.StageId, 2)], Months(4)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.DuplicateStage");
    }

    [Fact]
    public async Task A_promotion_whose_groups_carry_no_partition_is_refused_by_name()
    {
        await using var db = TestHarness.NewContext(nameof(A_promotion_whose_groups_carry_no_partition_is_refused_by_name));
        db.SeedCatalog();
        db.SeedStage(ChirurgieId, "Chirurgie");
        db.SeedGroup(groupId: 1, groupNumber: 1);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyRotationCycleCommand(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 2), new RotationStage(ChirurgieId, 2)], Months(4)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.NoPartitions");
    }

    [Fact]
    public async Task Two_blocks_of_one_level_hold_their_own_axes_side_by_side()
    {
        await using var db = TestHarness.NewContext(nameof(Two_blocks_of_one_level_hold_their_own_axes_side_by_side));
        SeedBlock(db);
        db.SeedStage(3, "Pédiatrie");
        db.SeedStage(4, "Gynécologie");
        await db.SaveChangesAsync();

        // The new CNPN's 3rd year: two semesters, each its own block, on windows that do not overlap.
        var semester1 = Months(2);
        var semester2 = Enumerable.Range(0, 2)
            .Select(i =>
            {
                var start = new DateOnly(2026, 2, 1).AddMonths(i);
                return new DateWindow(start, start.AddMonths(1).AddDays(-1));
            })
            .ToList();

        var first = await ApplyHandler(db).Handle(
            new ApplyRotationCycleCommand(
                TestHarness.LevelId, [new RotationStage(TestHarness.StageId, 1), new RotationStage(ChirurgieId, 1)], semester1), default);
        var second = await ApplyHandler(db).Handle(
            new ApplyRotationCycleCommand(TestHarness.LevelId, [new RotationStage(3, 1), new RotationStage(4, 1)], semester2), default);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        // Applying the second block must not have touched the first — replacement is scoped to the
        // stages named in the command.
        (await db.StageSlots.CountAsync()).Should().Be(8);
        (await db.StageSlots.CountAsync(s => s.StageId == TestHarness.StageId)).Should().Be(2);
        second.Value.SlotsReplaced.Should().Be(0);
    }
}
