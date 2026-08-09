using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.AssignRotationGroups;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Repartition;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Both strategies cut a promotion into equal partitions and the arranger cannot tell them apart.
/// They differ in *which* groups end up together, and therefore only in how the published répartition
/// reads: interleaved cells print <c>1, 3, 5, 7…</c>, contiguous cells print <c>1-4</c>.
/// </summary>
public class PartitionStrategyTests
{
    private static AssignRotationGroupsCommandHandler Handler(ApplicationDbContext db) => new(db);

    private static void SeedGroups(ApplicationDbContext db, int count)
    {
        db.SeedCatalog();
        for (int i = 1; i <= count; i++)
            db.SeedGroup(groupId: i, groupNumber: i);
    }

    [Fact]
    public async Task Interleaved_stripes_the_promotion_by_the_partition_count()
    {
        await using var db = TestHarness.NewContext(nameof(Interleaved_stripes_the_promotion_by_the_partition_count));
        SeedGroups(db, 8);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Partitions.Should().BeEquivalentTo(new[]
        {
            new PartitionMembership("A", 4, "1, 3, 5, 7"),
            new PartitionMembership("B", 4, "2, 4, 6, 8"),
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Contiguous_cuts_the_promotion_into_blocks_that_collapse_to_ranges()
    {
        await using var db = TestHarness.NewContext(nameof(Contiguous_cuts_the_promotion_into_blocks_that_collapse_to_ranges));
        SeedGroups(db, 8);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new AssignRotationGroupsCommand(
                TestHarness.CurrentYearId, 2, TestHarness.LevelId, PartitionStrategy.Contiguous),
            default);

        // This is the whole point of the strategy: the printed cell reads as a range.
        result.Value.Partitions.Should().BeEquivalentTo(new[]
        {
            new PartitionMembership("A", 4, "1-4"),
            new PartitionMembership("B", 4, "5-8"),
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Interleaved_with_three_partitions_steps_by_three()
    {
        await using var db = TestHarness.NewContext(nameof(Interleaved_with_three_partitions_steps_by_three));
        SeedGroups(db, 9);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 3, TestHarness.LevelId), default);

        result.Value.Partitions.Select(p => p.GroupNumbers)
            .Should().BeEquivalentTo(["1, 4, 7", "2, 5, 8", "3, 6, 9"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task An_uneven_promotion_spreads_the_remainder_over_the_leading_partitions()
    {
        await using var db = TestHarness.NewContext(nameof(An_uneven_promotion_spreads_the_remainder_over_the_leading_partitions));
        SeedGroups(db, 7);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new AssignRotationGroupsCommand(
                TestHarness.CurrentYearId, 3, TestHarness.LevelId, PartitionStrategy.Contiguous),
            default);

        // 7 over 3 → 3, 2, 2. Never 2, 2, 3 with the remainder dumped on the last partition.
        result.Value.Partitions.Select(p => p.GroupCount).Should().BeEquivalentTo([3, 2, 2],
            o => o.WithStrictOrdering());
        result.Value.Partitions.Select(p => p.GroupNumbers)
            .Should().BeEquivalentTo(["1-3", "4-5", "6-7"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Both_strategies_produce_partitions_of_the_same_size()
    {
        await using var db1 = TestHarness.NewContext("size-interleaved");
        await using var db2 = TestHarness.NewContext("size-contiguous");
        SeedGroups(db1, 13);
        SeedGroups(db2, 13);
        await db1.SaveChangesAsync();
        await db2.SaveChangesAsync();

        var interleaved = await Handler(db1).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 4, TestHarness.LevelId), default);
        var contiguous = await Handler(db2).Handle(
            new AssignRotationGroupsCommand(
                TestHarness.CurrentYearId, 4, TestHarness.LevelId, PartitionStrategy.Contiguous),
            default);

        // The property the arranger depends on, and the reason the two are interchangeable to it.
        contiguous.Value.Partitions.Select(p => p.GroupCount)
            .Should().BeEquivalentTo(interleaved.Value.Partitions.Select(p => p.GroupCount));
    }

    [Fact]
    public async Task A_second_run_leaves_an_existing_partitioning_alone()
    {
        await using var db = TestHarness.NewContext(nameof(A_second_run_leaves_an_existing_partitioning_alone));
        SeedGroups(db, 8);
        await db.SaveChangesAsync();

        await Handler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        // Without Reassign, asking for a different strategy is a no-op — a re-run must never reshuffle
        // a plan already built on the current partitioning.
        var second = await Handler(db).Handle(
            new AssignRotationGroupsCommand(
                TestHarness.CurrentYearId, 2, TestHarness.LevelId, PartitionStrategy.Contiguous),
            default);

        second.Value.Labeled.Should().Be(0);
        second.Value.Reassigned.Should().Be(0);
        second.Value.Partitions.Single(p => p.Label == "A").GroupNumbers.Should().Be("1, 3, 5, 7");
    }

    [Fact]
    public async Task Reassign_re_cuts_a_promotion_that_is_already_partitioned()
    {
        await using var db = TestHarness.NewContext(nameof(Reassign_re_cuts_a_promotion_that_is_already_partitioned));
        SeedGroups(db, 8);
        await db.SaveChangesAsync();

        await Handler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        var recut = await Handler(db).Handle(
            new AssignRotationGroupsCommand(
                TestHarness.CurrentYearId, 2, TestHarness.LevelId, PartitionStrategy.Contiguous,
                Reassign: true),
            default);

        recut.Value.Partitions.Single(p => p.Label == "A").GroupNumbers.Should().Be("1-4");
        // A={1,3,5,7} B={2,4,6,8} becomes A={1,2,3,4} B={5,6,7,8}: only 2 and 4 move into A, only
        // 5 and 7 move out of it. 1, 3 were already in A and 6, 8 already in B.
        recut.Value.Reassigned.Should().Be(4);
    }

    [Fact]
    public async Task Filling_the_gaps_tops_up_the_smaller_partition_rather_than_restarting_the_count()
    {
        await using var db = TestHarness.NewContext(nameof(Filling_the_gaps_tops_up_the_smaller_partition_rather_than_restarting_the_count));
        db.SeedCatalog();
        db.SeedGroup(groupId: 1, groupNumber: 1, rotationGroup: "A");
        db.SeedGroup(groupId: 2, groupNumber: 2, rotationGroup: "A");
        db.SeedGroup(groupId: 3, groupNumber: 3, rotationGroup: "A");
        db.SeedGroup(groupId: 4, groupNumber: 4, rotationGroup: "B");
        db.SeedGroup(groupId: 5, groupNumber: 5);
        db.SeedGroup(groupId: 6, groupNumber: 6);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        // Existing members count toward the balance, so both new groups go to B — the alternation is not
        // restarted from zero, which would have left A with five of six.
        result.Value.Labeled.Should().Be(2);
        result.Value.Partitions.Should().BeEquivalentTo(new[]
        {
            new PartitionMembership("A", 3, "1-3"),
            new PartitionMembership("B", 3, "4-6"),
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task A_gap_fill_keeps_the_partition_count_the_promotion_already_has()
    {
        await using var db = TestHarness.NewContext(nameof(A_gap_fill_keeps_the_partition_count_the_promotion_already_has));
        db.SeedCatalog();
        db.SeedGroup(groupId: 1, groupNumber: 1, rotationGroup: "A");
        db.SeedGroup(groupId: 2, groupNumber: 2, rotationGroup: "A");
        db.SeedGroup(groupId: 3, groupNumber: 3);
        db.SeedGroup(groupId: 4, groupNumber: 4);
        await db.SaveChangesAsync();

        // Only A is in use, so this promotion has ONE partition and asking for two does not silently
        // introduce a second: BuildLabels lets the existing structure win, because a gap-fill must never
        // re-cut a promotion a plan may already be built on.
        var filled = await Handler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        filled.Value.Partitions.Should().BeEquivalentTo(
            new[] { new PartitionMembership("A", 4, "1-4") });

        // Reassign is the way to actually change the count.
        var recut = await Handler(db).Handle(
            new AssignRotationGroupsCommand(
                TestHarness.CurrentYearId, 2, TestHarness.LevelId, PartitionStrategy.Contiguous,
                Reassign: true),
            default);

        recut.Value.Partitions.Should().BeEquivalentTo(new[]
        {
            new PartitionMembership("A", 2, "1-2"),
            new PartitionMembership("B", 2, "3-4"),
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Reassign_is_refused_while_any_cell_of_the_promotion_is_published()
    {
        await using var db = TestHarness.NewContext(nameof(Reassign_is_refused_while_any_cell_of_the_promotion_is_published));
        var stage = db.SeedCatalog();
        var service = db.SeedService(2, "Cardiologie");

        var group = db.SeedGroup(groupId: 1, groupNumber: 1, rotationGroup: "A");
        db.SeedGroup(groupId: 2, groupNumber: 2, rotationGroup: "B");

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

        var result = await Handler(db).Handle(
            new AssignRotationGroupsCommand(
                TestHarness.CurrentYearId, 2, TestHarness.LevelId, PartitionStrategy.Contiguous,
                Reassign: true),
            default);

        // Students have been sent there. Re-cutting under a published plan would leave it describing a
        // partitioning that no longer exists.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Partitions.CannotReassignPublished");
    }

    [Fact]
    public async Task Reassign_reports_the_planned_cells_an_arrange_must_now_rebuild()
    {
        await using var db = TestHarness.NewContext(nameof(Reassign_reports_the_planned_cells_an_arrange_must_now_rebuild));
        var stage = db.SeedCatalog();
        var service = db.SeedService(2, "Cardiologie");
        var slot = db.SeedSlot(stage, slotId: 1, periodNumber: 1,
            new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));

        for (int i = 1; i <= 4; i++)
        {
            var group = db.SeedGroup(groupId: i, groupNumber: i);
            db.SeedSlotAssignment(id: i, db.SeedCohortFor(stage, group, cohortId: 30 + i), slot, service);
        }
        await db.SaveChangesAsync();

        await Handler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        var recut = await Handler(db).Handle(
            new AssignRotationGroupsCommand(
                TestHarness.CurrentYearId, 2, TestHarness.LevelId, PartitionStrategy.Contiguous,
                Reassign: true),
            default);

        // Planned but unpublished: not touched, but placed for a partition the group may have left, so
        // the caller has to know an arrange is owed.
        recut.Value.PlannedCellsAffected.Should().Be(4);
        recut.Value.Reassigned.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_allocator_never_leaves_a_group_unlabelled_whatever_the_shape()
    {
        // An unlabelled group is invisible to partition filtering, so it would silently drop out of
        // every arrange scoped to a partition.
        foreach (int count in new[] { 1, 2, 3, 5, 8, 13, 80 })
        {
            foreach (int partitions in new[] { 1, 2, 3, 4, 7 })
            {
                var ids = Enumerable.Range(1, count).ToList();

                foreach (var strategy in new[] { PartitionStrategy.Interleaved, PartitionStrategy.Contiguous })
                {
                    var assigned = PartitionAllocator.ReassignAll(ids, partitions, strategy);

                    assigned.Should().HaveCount(count,
                        $"{count} groups over {partitions} partitions ({strategy})");
                    assigned.Values.Distinct().Count().Should().BeLessThanOrEqualTo(
                        Math.Min(partitions, count));
                }
            }
        }
    }

    [Fact]
    public void Contiguous_partitions_are_the_only_ones_whose_cells_collapse()
    {
        var ids = Enumerable.Range(1, 80).ToList();

        var contiguous = PartitionAllocator.ReassignAll(ids, 2, PartitionStrategy.Contiguous);
        var interleaved = PartitionAllocator.ReassignAll(ids, 2, PartitionStrategy.Interleaved);

        string ContiguousA = GroupNumberRanges.Format(contiguous.Where(kv => kv.Value == "A").Select(kv => kv.Key));
        string InterleavedA = GroupNumberRanges.Format(interleaved.Where(kv => kv.Value == "A").Select(kv => kv.Key));

        // The faculty's own published table reads like the first of these.
        ContiguousA.Should().Be("1-40");
        InterleavedA.Should().StartWith("1, 3, 5, 7");
        InterleavedA.Split(',').Should().HaveCount(40);
    }
}
