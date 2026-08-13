using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.AssignRotationGroups;
using PGSH.Application.Stages.Planning;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Undoing a partitioning. The interesting part is not the clearing but what it protects: a cut nobody has
/// been sent to yet is a mistake, and a cut students have already been sent under is a published document.
/// </summary>
public class ClearRotationGroupsTests
{
    private static ClearRotationGroupsCommandHandler ClearHandler(ApplicationDbContext db) => new(db);

    private static AssignRotationGroupsCommandHandler AssignHandler(ApplicationDbContext db) => new(db);

    private static void SeedGroups(ApplicationDbContext db, int count, string? label = null)
    {
        db.SeedCatalog();
        for (int i = 1; i <= count; i++)
            db.SeedGroup(groupId: i, groupNumber: i, rotationGroup: label);
    }

    [Fact]
    public async Task Clearing_removes_every_label_and_says_how_many()
    {
        await using var db = TestHarness.NewContext(nameof(Clearing_removes_every_label_and_says_how_many));
        SeedGroups(db, 6);
        await db.SaveChangesAsync();

        await AssignHandler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 3, TestHarness.LevelId), default);

        var result = await ClearHandler(db).Handle(
            new ClearRotationGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cleared.Should().Be(6);
        result.Value.TotalGroups.Should().Be(6);

        (await db.AcademicGroups.CountAsync(g => g.RotationGroup != null)).Should().Be(0);
    }

    /// <summary>
    /// The reason this command exists. <c>BuildLabels</c> lets the *existing* partition count win over the
    /// requested one — deliberately, so a gap-fill cannot reshuffle a plan built on the current cut. The
    /// consequence is that a promotion mistakenly cut into two stays two-way for every later assign,
    /// whatever count is asked for, and clearing is the only way out.
    /// </summary>
    [Fact]
    public async Task A_wrong_partition_count_can_only_be_changed_after_clearing()
    {
        await using var db = TestHarness.NewContext(nameof(A_wrong_partition_count_can_only_be_changed_after_clearing));
        SeedGroups(db, 10);
        await db.SaveChangesAsync();

        // The mistake: two partitions, where the block needs ten.
        await AssignHandler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        // Asking again for ten changes nothing — every group already carries a label, so there is nothing
        // to fill and the existing count of two stands.
        var reasked = await AssignHandler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 10, TestHarness.LevelId), default);

        reasked.Value.Partitions.Should().HaveCount(2);

        await ClearHandler(db).Handle(
            new ClearRotationGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        var recut = await AssignHandler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 10, TestHarness.LevelId), default);

        recut.Value.Partitions.Should().HaveCount(10);
        recut.Value.Partitions.Should().OnlyContain(p => p.GroupCount == 1);
    }

    [Fact]
    public async Task Clearing_is_refused_while_any_cell_of_the_promotion_is_published()
    {
        await using var db = TestHarness.NewContext(nameof(Clearing_is_refused_while_any_cell_of_the_promotion_is_published));
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

        var result = await ClearHandler(db).Handle(
            new ClearRotationGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Partitions.CannotClearPublished");

        // Refused means untouched, not partially applied.
        (await db.AcademicGroups.CountAsync(g => g.RotationGroup != null)).Should().Be(2);
    }

    /// <summary>
    /// The user's actual worry: does removing a partition take the planning with it? It does not. Nothing
    /// points at a label — cohorts belong to groups, cells to cohorts and slots, periods to cells — so
    /// clearing removes no row. What it costs is that the cells no longer describe any partition, and that
    /// is reported rather than silently absorbed.
    /// </summary>
    [Fact]
    public async Task Clearing_destroys_no_cohort_cell_assignment_or_period()
    {
        await using var db = TestHarness.NewContext(nameof(Clearing_destroys_no_cohort_cell_assignment_or_period));
        var stage = db.SeedCatalog();
        var service = db.SeedService(2, "Cardiologie");
        var slot = db.SeedSlot(stage, slotId: 1, periodNumber: 1,
            new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));

        for (int i = 1; i <= 4; i++)
        {
            var group = db.SeedGroup(groupId: i, groupNumber: i, rotationGroup: i % 2 == 1 ? "A" : "B");
            var cohort = db.SeedCohortFor(stage, group, cohortId: 30 + i);
            db.SeedSlotAssignment(id: i, cohort, slot, service);

            // An unpublished period: attached to the student's assignment but to no cell, which is
            // exactly the shape the legacy import left behind.
            var registration = db.SeedRegistration($"Etudiant{i}", "Test", group);
            var assignment = db.SeedAssignment(registration, cohort);
            db.SeedPeriod(assignment, service, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));
        }
        await db.SaveChangesAsync();

        var result = await ClearHandler(db).Handle(
            new ClearRotationGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cleared.Should().Be(4);

        // Every planned cell was placed for a partition that no longer exists, so an arrange is owed.
        result.Value.PlannedCellsAffected.Should().Be(4);

        (await db.Cohorts.CountAsync()).Should().Be(4);
        (await db.CohortSlotAssignments.CountAsync()).Should().Be(4);
        (await db.InternshipAssignments.CountAsync()).Should().Be(4);
        (await db.ServicePeriods.CountAsync()).Should().Be(4);
        (await db.StageSlots.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Clearing_one_level_leaves_another_levels_partitions_alone()
    {
        await using var db = TestHarness.NewContext(nameof(Clearing_one_level_leaves_another_levels_partitions_alone));
        db.SeedCatalog();

        const int otherLevelId = TestHarness.LevelId + 1;
        db.SeedLevel(otherLevelId, "4ème année Médecine", 4);

        for (int i = 1; i <= 3; i++)
            db.SeedGroup(groupId: i, groupNumber: i, rotationGroup: "A");

        for (int i = 4; i <= 6; i++)
        {
            var group = db.SeedGroup(groupId: i, groupNumber: i, rotationGroup: "B");
            group.LevelId = otherLevelId;
        }
        await db.SaveChangesAsync();

        await ClearHandler(db).Handle(
            new ClearRotationGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        // Partitions are scoped per (year, level) — two promotions can carry different counts, and
        // clearing one must not disturb the other.
        (await db.AcademicGroups.CountAsync(g => g.RotationGroup == null)).Should().Be(3);
        (await db.AcademicGroups.CountAsync(g => g.RotationGroup == "B")).Should().Be(3);
    }

    [Fact]
    public async Task Clearing_an_unpartitioned_promotion_is_a_no_op_rather_than_an_error()
    {
        await using var db = TestHarness.NewContext(nameof(Clearing_an_unpartitioned_promotion_is_a_no_op_rather_than_an_error));
        SeedGroups(db, 4);
        await db.SaveChangesAsync();

        var result = await ClearHandler(db).Handle(
            new ClearRotationGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cleared.Should().Be(0);
        result.Value.TotalGroups.Should().Be(4);
        result.Value.PlannedCellsAffected.Should().Be(0);
    }

    [Fact]
    public async Task Clearing_a_promotion_with_no_groups_reports_nothing_rather_than_failing()
    {
        await using var db = TestHarness.NewContext(nameof(Clearing_a_promotion_with_no_groups_reports_nothing_rather_than_failing));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await ClearHandler(db).Handle(
            new ClearRotationGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalGroups.Should().Be(0);
    }

    /// <summary>
    /// After clearing, the strategy is free again — which is the practical reason to clear rather than
    /// re-cut: <c>Contiguous</c> on a promotion already striped by <c>Interleaved</c> needs the labels gone
    /// or the existing count constrains it.
    /// </summary>
    [Fact]
    public async Task A_cleared_promotion_can_be_re_cut_with_the_other_strategy()
    {
        await using var db = TestHarness.NewContext(nameof(A_cleared_promotion_can_be_re_cut_with_the_other_strategy));
        SeedGroups(db, 8);
        await db.SaveChangesAsync();

        await AssignHandler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        await ClearHandler(db).Handle(
            new ClearRotationGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        var recut = await AssignHandler(db).Handle(
            new AssignRotationGroupsCommand(
                TestHarness.CurrentYearId, 2, TestHarness.LevelId, PartitionStrategy.Contiguous),
            default);

        recut.Value.Partitions.Select(p => p.GroupNumbers)
            .Should().BeEquivalentTo(["1-4", "5-8"], o => o.WithStrictOrdering());
    }
}
