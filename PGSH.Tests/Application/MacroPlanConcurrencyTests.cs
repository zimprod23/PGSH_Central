using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.MacroPlan;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Hospitals;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// What happens when several partitions occupy one stage at the same time — <c>Lₛ = P·kₛ/T</c> of
/// them, which is more than one exactly when the block's stages have unequal durations.
///
/// <para>The shape under test is the 5th year's (<c>example_stage_assignement/demo/MED05.png</c>):
/// Gynécologie runs <c>k=3</c> against six stages of <c>k=1</c>, so <c>T=9</c>, <c>P=9</c> and
/// <c>L=3</c> — three partitions, twenty groups, five services, for three columns at a stretch. The
/// faculty's own document puts four groups in each service. So must we.</para>
///
/// <para>⚠ The failure this pins down is not a wrong arrangement but a wrong <i>call shape</i>: the
/// macro plan used to arrange one partition per call, so each was balanced over the full service
/// list in ignorance of the other two. Their surpluses then stacked — <c>BuildServiceQueue</c>
/// hands the remainder to the same leading services every time, and every partition of a column
/// carries the same rotation offset — giving <b>6/5/3/3/3</b> where <b>4/4/4/4/4</b> was owed. Two
/// services held twice their share and nothing reported it.</para>
/// </summary>
public class MacroPlanConcurrencyTests
{
    private const int Gyneco = TestHarness.StageId;
    private static readonly int[] ServiceIds = [10, 11, 12, 13, 14];

    /// <summary>Twenty groups over three partitions (7/7/6, as 60 groups over 9 partitions gives),
    /// interleaved by number the way <see cref="PartitionAllocator"/> cuts them.</summary>
    private static readonly string[] Labels = ["A", "B", "C"];

    private static GenerateMacroPlanCommandHandler Handler(ApplicationDbContext db) =>
        new(db,
            new CohortProvisioner(db),
            new StudentAffectationService(db),
            db.Arranger(),
            new SchedulePublisher(db, new ServiceOccupancyCalculator(db), new ServiceIntakeCalculator(db)));

    /// <summary>
    /// One stage of five services, twenty groups in three partitions, and a three-column run — with
    /// twelve students in every group, so the queue is weighted the way production weights it rather
    /// than falling back to raw capacity proportions.
    /// </summary>
    private static void SeedBlock(ApplicationDbContext db, int columns = 3)
    {
        var stage = db.SeedCatalog();

        foreach (int id in ServiceIds)
            stage.AllowedServices.Add(db.SeedService(id, $"Gynéco {id}"));

        for (int period = 1; period <= columns; period++)
        {
            db.SeedSlot(stage, period, period,
                new DateOnly(2025, 11, 1).AddDays((period - 1) * 30),
                new DateOnly(2025, 11, 30).AddDays((period - 1) * 30));
        }

        for (int number = 1; number <= 20; number++)
        {
            var group = db.SeedGroup(number, number, Labels[(number - 1) % Labels.Length]);
            for (int s = 0; s < 12; s++)
                db.SeedRegistration($"E{number}", $"S{s}", group);
        }
    }

    private static GenerateMacroPlanCommand PlanFor(params int[] periods) =>
        new(TestHarness.CurrentYearId,
            Labels.Select(l => new PartitionStagePlan(l, Gyneco, periods)).ToList(),
            AssignStudents: true, AutoArrange: true, Publish: false);

    /// <summary>Groups per service, per column.</summary>
    private static async Task<Dictionary<int, Dictionary<int, int>>> LoadAsync(ApplicationDbContext db) =>
        (await db.CohortSlotAssignments
            .Include(a => a.StageSlot)
            .ToListAsync())
        .GroupBy(a => a.StageSlot.PeriodNumber)
        .ToDictionary(
            g => g.Key,
            g => g.GroupBy(a => a.ServiceId).ToDictionary(s => s.Key, s => s.Count()));

    [Fact]
    public async Task Concurrent_partitions_share_a_stage_evenly_in_every_column()
    {
        await using var db = TestHarness.NewContext("macro-concurrency-balanced");
        SeedBlock(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(PlanFor(1, 2, 3), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.CellsArranged.Should().Be(60, "20 groups × 3 columns");
        result.Value.GroupConflicts.Should().Be(0);

        var byColumn = await LoadAsync(db);
        byColumn.Should().HaveCount(3);

        foreach (var (period, byService) in byColumn)
        {
            byService.Should().HaveCount(5, $"every service is used in column {period}");
            byService.Values.Should().AllBeEquivalentTo(4,
                $"20 groups over 5 services is 4 each — column {period} is what the faculty prints");
        }
    }

    [Fact]
    public async Task A_partition_still_moves_through_the_stage_rather_than_sitting_in_one_service()
    {
        // Balance across the column must not be bought by freezing each group in place: a period is
        // one *service*, so a group doing three periods of Gynécologie passes through three of them.
        await using var db = TestHarness.NewContext("macro-concurrency-rotates");
        SeedBlock(db);
        await db.SaveChangesAsync();

        await Handler(db).Handle(PlanFor(1, 2, 3), default);

        var byCohort = (await db.CohortSlotAssignments.ToListAsync())
            .GroupBy(a => a.CohortId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.ServiceId).Distinct().Count());

        byCohort.Should().HaveCount(20);
        byCohort.Values.Should().AllBeEquivalentTo(3, "three columns, three different services");
    }

    [Fact]
    public async Task One_partition_alone_is_arranged_exactly_as_before()
    {
        // L = 1 is the uniform-duration case and the overwhelming majority of the matrix. Grouping
        // must be a no-op there: a block of one is one call, with the queue it always had.
        await using var db = TestHarness.NewContext("macro-concurrency-single");
        SeedBlock(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GenerateMacroPlanCommand(
                TestHarness.CurrentYearId,
                [new PartitionStagePlan("A", Gyneco, [1])],
                AssignStudents: true, AutoArrange: true, Publish: false),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.CellsArranged.Should().Be(7, "partition A holds groups 1, 4, 7, 10, 13, 16, 19");

        var byService = (await LoadAsync(db))[1];
        byService.Values.Sum().Should().Be(7);
        byService.Values.Max().Should().Be(2, "7 over 5 services is 2, 2, 1, 1, 1");
    }

    [Fact]
    public void Runs_of_one_stage_that_differ_are_separate_blocks()
    {
        // ⚠ The key is the window as well as the stage. The 5th year has {A,B,C} in périodes 1-3,
        // {E,H,I} in 4-6 and {D,F,G} in 7-9 of the *same* stage — three concurrency blocks of three,
        // not one of nine. Grouping by stage alone would balance twenty groups against a queue built
        // for sixty and put the wrong partitions in the same column's arithmetic.
        var blocks = ConcurrencyBlock.From([
            new("A", 8, [1, 2, 3]), new("B", 8, [1, 2, 3]), new("C", 8, [1, 2, 3]),
            new("E", 8, [4, 5, 6]), new("H", 8, [4, 5, 6]), new("I", 8, [4, 5, 6]),
            new("D", 8, [7, 8, 9]), new("F", 8, [7, 8, 9]), new("G", 8, [7, 8, 9]),
            new("A", 9, [4]),
        ]);

        blocks.Should().HaveCount(4);

        blocks.Should()
            .ContainSingle(b => b.StageId == 8 && b.PeriodNumbers.SequenceEqual(new[] { 7, 8, 9 }))
            .Which.RotationGroups.Should().Equal("D", "F", "G");

        blocks.Should()
            .ContainSingle(b => b.StageId == 9)
            .Which.RotationGroups.Should().Equal("A");
    }

    [Fact]
    public void An_absent_window_means_every_period_and_is_not_a_crash()
    {
        // « vide = toutes » is what the matrix tells the admin, and the request body may leave the
        // field out altogether. Grouping reads the window to build its key, so a null here would be
        // a 500 on a request that has always been valid.
        var blocks = ConcurrencyBlock.From([
            new("A", 8, null!),
            new("B", 8, []),
            new("C", 8, [1]),
        ]);

        blocks.Should().HaveCount(2);
        blocks.Should().ContainSingle(b => b.PeriodNumbers.Count == 0)
            .Which.RotationGroups.Should().Equal("A", "B");
    }

    [Fact]
    public void A_window_is_matched_by_content_not_by_the_order_it_was_written_in()
    {
        var blocks = ConcurrencyBlock.From([
            new("A", 8, [3, 1, 2]),
            new("B", 8, [1, 2, 3]),
        ]);

        blocks.Should().ContainSingle().Which.RotationGroups.Should().Equal("A", "B");
        blocks[0].PeriodNumbers.Should().Equal(1, 2, 3);
    }
}
