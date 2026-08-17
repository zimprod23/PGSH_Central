using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Who is allowed to cut a promotion into partitions, and on what number.
///
/// <para>⚠ The arranger used to fall back to <c>services.Count</c> — the number of services the
/// <em>stage</em> allows — whenever the caller named no count and no group carried a label. A stage's
/// service list is not a statement about how a promotion should be divided, and the failure is silent
/// and sticky: Santé Publique has one service, so arranging it first cut the whole 5th year one-way,
/// and every stage arranged afterwards inherited that single partition because
/// <c>PartitionAllocator.BuildLabels</c> lets an existing cut win over any requested count.</para>
///
/// <para>Cutting a promotion is <c>AssignRotationGroupsCommand</c>'s job — it takes a strategy, an
/// explicit count, refuses across a published cell and writes an audit entry. Inventing one here
/// bypassed all four.</para>
/// </summary>
public class ArrangerPartitioningTests
{
    private const int OnlyService = 10;

    /// <summary>One stage of a single service, four rosters, two periods — the shape that used to
    /// produce a one-way cut of the whole promotion.</summary>
    private static void SeedOneServiceStage(ApplicationDbContext db, string? label = null)
    {
        var stage = db.SeedCatalog();
        stage.AllowedServices.Add(db.SeedService(OnlyService, "Santé Publique"));

        db.SeedSlot(stage, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 25));
        db.SeedSlot(stage, 2, 2, new DateOnly(2025, 11, 26), new DateOnly(2025, 12, 16));

        for (int n = 1; n <= 4; n++)
            db.SeedCohortFor(stage, db.SeedGroup(n, n, rotationGroup: label), 100 + n);
    }

    [Fact]
    public async Task Arranging_does_not_invent_a_partitioning_from_the_service_list()
    {
        await using var db = TestHarness.NewContext(nameof(Arranging_does_not_invent_a_partitioning_from_the_service_list));
        SeedOneServiceStage(db);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Assigned.Should().Be(8, "4 rosters × 2 periods — the arrange still happens");

        (await db.AcademicGroups.ToListAsync())
            .Should().OnlyContain(g => g.RotationGroup == null,
                "nobody asked for a cut, so the promotion keeps not having one");
    }

    [Fact]
    public async Task Targeting_a_partition_on_a_promotion_nobody_cut_is_reported()
    {
        // The dangerous shape: before, the fallback silently created a partition « A » out of the
        // one-service list, the filter then matched it, and the run "succeeded" on a division nobody
        // had authored.
        await using var db = TestHarness.NewContext(nameof(Targeting_a_partition_on_a_promotion_nobody_cut_is_reported));
        SeedOneServiceStage(db);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, ["A"], null, null, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.PromotionNotPartitioned");
        result.Error.Description.Should().Contain("3ème année").And.Contain("Cardiologie");

        (await db.CohortSlotAssignments.CountAsync()).Should().Be(0);
        (await db.AcademicGroups.ToListAsync()).Should().OnlyContain(g => g.RotationGroup == null);
    }

    [Fact]
    public async Task An_explicit_count_still_cuts_the_promotion()
    {
        await using var db = TestHarness.NewContext(nameof(An_explicit_count_still_cuts_the_promotion));
        SeedOneServiceStage(db);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, partitionCount: 2, default);

        result.IsSuccess.Should().BeTrue();

        var labels = (await db.AcademicGroups.OrderBy(g => g.GroupNumber).ToListAsync())
            .Select(g => g.RotationGroup);
        labels.Should().Equal("A", "B", "A", "B");
    }

    [Fact]
    public async Task A_gap_in_an_existing_cut_is_filled_without_a_count()
    {
        // The other half of the rule: an existing cut is authoritative, so topping up the rosters that
        // joined the promotion later needs no count and must not re-cut anything.
        await using var db = TestHarness.NewContext(nameof(A_gap_in_an_existing_cut_is_filled_without_a_count));
        var stage = db.SeedCatalog();
        stage.AllowedServices.Add(db.SeedService(OnlyService, "Santé Publique"));
        db.SeedSlot(stage, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 25));

        db.SeedCohortFor(stage, db.SeedGroup(1, 1, rotationGroup: "A"), 101);
        db.SeedCohortFor(stage, db.SeedGroup(2, 2, rotationGroup: "B"), 102);
        db.SeedCohortFor(stage, db.SeedGroup(3, 3), 103);
        db.SeedCohortFor(stage, db.SeedGroup(4, 4), 104);
        await db.SaveChangesAsync();

        await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        var labels = (await db.AcademicGroups.OrderBy(g => g.GroupNumber).ToListAsync())
            .Select(g => g.RotationGroup);
        labels.Should().Equal(["A", "B", "A", "B"], "the existing two-way cut absorbs the new rosters");
    }

    /// <summary>
    /// The cut belongs to the promotion, and a stage sees only part of it.
    ///
    /// <para>⚠ Measured on Med6 (2026-08-13): a promotion re-cut into ten clean partitions came out
    /// A = 42, B = 42, C–J = 2 each. <c>PartitionAllocator.BuildLabels</c> takes "the existing count"
    /// from the labels it is shown, and it was shown the cohorts of one stage — where only A and B
    /// happened to appear. Every unlabelled roster was then filled into those two, permanently, and
    /// the crossover built on them is nonsense.</para>
    /// </summary>
    [Fact]
    public async Task The_partition_count_comes_from_the_promotion_not_from_this_stage_s_cohorts()
    {
        await using var db = TestHarness.NewContext(nameof(The_partition_count_comes_from_the_promotion_not_from_this_stage_s_cohorts));
        var stage = db.SeedCatalog();
        stage.AllowedServices.Add(db.SeedService(OnlyService, "Santé Publique"));
        db.SeedSlot(stage, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 25));

        // The promotion is cut four ways. Only A and B have a cohort in this stage — C and D are in it
        // through some other stage, which is the ordinary state of a partially provisioned promotion.
        var a = db.SeedGroup(1, 1, rotationGroup: "A");
        var b = db.SeedGroup(2, 2, rotationGroup: "B");
        db.SeedGroup(3, 3, rotationGroup: "C");
        db.SeedGroup(4, 4, rotationGroup: "D");
        db.SeedCohortFor(stage, a, 101);
        db.SeedCohortFor(stage, b, 102);

        // Four rosters joined later and carry no partition. They do have a cohort here.
        for (int n = 5; n <= 8; n++)
            db.SeedCohortFor(stage, db.SeedGroup(n, n), 100 + n);

        // …and one that has no cohort in this stage at all.
        db.SeedGroup(9, 9);
        await db.SaveChangesAsync();

        await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        var labels = (await db.AcademicGroups.OrderBy(g => g.GroupNumber).ToListAsync())
            .ToDictionary(g => g.GroupNumber, g => g.RotationGroup);

        labels[7].Should().Be("C");
        labels[8].Should().Be("D", "the promotion has four partitions, not the two this stage can see");
        labels[9].Should().BeNull("an arrange labels only the rosters it is actually placing");

        labels.Values.Where(l => l is not null).Distinct().Should().HaveCount(4);
    }

    /// <summary>
    /// The mirror image: the stage's own cohorts are all unlabelled, so the arranger concluded the
    /// promotion had never been cut — and either refused a legitimate partition target or left the
    /// rosters unlabelled and invisible to every later filter.
    /// </summary>
    [Fact]
    public async Task An_existing_cut_is_seen_even_when_no_cohort_of_this_stage_carries_it()
    {
        await using var db = TestHarness.NewContext(nameof(An_existing_cut_is_seen_even_when_no_cohort_of_this_stage_carries_it));
        var stage = db.SeedCatalog();
        stage.AllowedServices.Add(db.SeedService(OnlyService, "Santé Publique"));
        db.SeedSlot(stage, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 25));

        db.SeedGroup(1, 1, rotationGroup: "A");
        db.SeedGroup(2, 2, rotationGroup: "B");
        db.SeedCohortFor(stage, db.SeedGroup(3, 3), 103);
        db.SeedCohortFor(stage, db.SeedGroup(4, 4), 104);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();

        var labels = (await db.AcademicGroups.OrderBy(g => g.GroupNumber).ToListAsync())
            .Select(g => g.RotationGroup);
        labels.Should().Equal(["A", "B", "A", "B"], "the promotion's two-way cut absorbs both rosters");
    }
}
