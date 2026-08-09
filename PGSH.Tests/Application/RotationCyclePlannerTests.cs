using FluentAssertions;
using PGSH.Application.Stages.RotationCycle;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The crossover rule, tested without a database because it needs none: a block of S stages at k
/// periods each occupies S × k columns, and partition p sits in stage <c>(p mod S + turn) mod S</c>.
/// </summary>
public class RotationCyclePlannerTests
{
    private const int Medecine = 1;
    private const int Chirurgie = 2;
    private const int Pediatrie = 3;

    /// <summary>n consecutive month-long windows starting in October — inclusive of both ends.</summary>
    private static List<(DateOnly, DateOnly)> Months(int count) =>
        Enumerable.Range(0, count)
            .Select(i =>
            {
                var start = new DateOnly(2025, 10, 1).AddMonths(i);
                return (start, start.AddMonths(1).AddDays(-1));
            })
            .ToList();

    [Fact]
    public void Two_stages_and_two_partitions_produce_the_mirror()
    {
        var layout = RotationCyclePlanner.Build(
            [Medecine, Chirurgie], periodsPerStage: 2, ["A", "B"], Months(4));

        layout.IsSuccess.Should().BeTrue();
        layout.Value.Columns.Should().HaveCount(4);

        // Médecine P1-P2 for A then P3-P4 for B; Chirurgie exactly the opposite.
        Plan(layout.Value, "A", Medecine).Should().BeEquivalentTo([1, 2]);
        Plan(layout.Value, "A", Chirurgie).Should().BeEquivalentTo([3, 4]);
        Plan(layout.Value, "B", Chirurgie).Should().BeEquivalentTo([1, 2]);
        Plan(layout.Value, "B", Medecine).Should().BeEquivalentTo([3, 4]);
    }

    [Fact]
    public void The_column_count_is_stages_times_periods_and_not_partitions_times_periods()
    {
        // Three stages, one period each, six partitions → three columns, two partitions per stage per
        // turn. Partitions subdivide who is where; they do not lengthen the timeline.
        var layout = RotationCyclePlanner.Build(
            [Medecine, Chirurgie, Pediatrie], 1, ["A", "B", "C", "D", "E", "F"], Months(3));

        layout.IsSuccess.Should().BeTrue();
        layout.Value.Columns.Should().HaveCount(3);
        layout.Value.Lanes.Should().HaveCount(3);
        layout.Value.Lanes.Should().AllSatisfy(l => l.Partitions.Should().HaveCount(2));
        layout.Value.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Six_windows_for_a_three_stage_block_of_one_period_is_refused_with_the_arithmetic()
    {
        var layout = RotationCyclePlanner.Build(
            [Medecine, Chirurgie, Pediatrie], 1, ["A", "B", "C"], Months(6));

        layout.IsFailure.Should().BeTrue();
        layout.Error.Code.Should().Be("RotationCycle.WrongWindowCount");
        layout.Error.Description.Should().Contain("3").And.Contain("6");
    }

    [Theory]
    [InlineData(2, 1, 2)]
    [InlineData(2, 2, 2)]
    [InlineData(3, 1, 3)]
    [InlineData(3, 2, 3)]
    [InlineData(4, 1, 4)]
    [InlineData(5, 2, 5)]
    [InlineData(7, 1, 7)]
    [InlineData(3, 1, 6)]
    [InlineData(2, 3, 6)]
    public void Every_partition_passes_through_every_stage_exactly_once(
        int stageCount, int periodsPerStage, int partitionCount)
    {
        var stages = Enumerable.Range(1, stageCount).ToList();
        var partitions = Enumerable.Range(0, partitionCount)
            .Select(i => ((char)('A' + i)).ToString())
            .ToList();

        var layout = RotationCyclePlanner.Build(
            stages, periodsPerStage, partitions, Months(stageCount * periodsPerStage));

        layout.IsSuccess.Should().BeTrue();

        foreach (string partition in partitions)
        {
            var visited = layout.Value.Matrix
                .Where(m => m.RotationGroup == partition)
                .Select(m => m.StageId)
                .ToList();

            visited.Should().BeEquivalentTo(stages, $"{partition} must do each stage once");
            visited.Should().OnlyHaveUniqueItems();
        }
    }

    [Theory]
    [InlineData(2, 2, 2)]
    [InlineData(3, 1, 3)]
    [InlineData(3, 2, 6)]
    [InlineData(4, 1, 4)]
    public void No_partition_is_ever_in_two_stages_at_once(
        int stageCount, int periodsPerStage, int partitionCount)
    {
        var stages = Enumerable.Range(1, stageCount).ToList();
        var partitions = Enumerable.Range(0, partitionCount)
            .Select(i => ((char)('A' + i)).ToString())
            .ToList();

        var layout = RotationCyclePlanner.Build(
            stages, periodsPerStage, partitions, Months(stageCount * periodsPerStage));

        // The property GroupScheduleConflictGuard would otherwise have to catch after the fact.
        foreach (string partition in partitions)
        {
            var occupied = layout.Value.Matrix
                .Where(m => m.RotationGroup == partition)
                .SelectMany(m => m.PeriodNumbers)
                .ToList();

            occupied.Should().OnlyHaveUniqueItems($"{partition} is in one place per column");
            occupied.Should().BeEquivalentTo(
                layout.Value.Columns.Select(c => c.PeriodNumber),
                "and is somewhere in every column");
        }
    }

    [Fact]
    public void Every_column_of_every_stage_is_covered_by_exactly_one_lane()
    {
        var layout = RotationCyclePlanner.Build(
            [Medecine, Chirurgie, Pediatrie], 2, ["A", "B", "C"], Months(6));

        foreach (int stage in new[] { Medecine, Chirurgie, Pediatrie })
        {
            var covered = layout.Value.Matrix
                .Where(m => m.StageId == stage)
                .SelectMany(m => m.PeriodNumbers)
                .ToList();

            // A stage holds someone in every column — that is what makes the published table full.
            covered.Should().BeEquivalentTo(layout.Value.Columns.Select(c => c.PeriodNumber));
        }
    }

    [Fact]
    public void More_partitions_than_stages_but_not_a_multiple_is_allowed_and_reported()
    {
        var layout = RotationCyclePlanner.Build(
            [Medecine, Chirurgie, Pediatrie], 1, ["A", "B", "C", "D"], Months(3));

        // Still a correct plan — every partition does every stage — but the turns are uneven, which is
        // a capacity surprise if it was not intended. Reported, never refused.
        layout.IsSuccess.Should().BeTrue();
        layout.Value.Warnings.Should().ContainSingle().Which.Should().Contain("4 partitions");
        layout.Value.Lanes[0].Partitions.Should().BeEquivalentTo(["A", "D"]);
    }

    [Fact]
    public void Fewer_partitions_than_stages_leaves_a_stage_empty_for_a_whole_turn()
    {
        var layout = RotationCyclePlanner.Build(
            [Medecine, Chirurgie, Pediatrie], 1, ["A", "B"], Months(3));

        layout.IsSuccess.Should().BeTrue();
        layout.Value.Warnings.Should().ContainSingle().Which.Should().Contain("sans partition");
    }

    [Fact]
    public void Overlapping_windows_are_refused_because_a_column_cannot_start_before_the_last_ends()
    {
        var overlapping = new List<(DateOnly, DateOnly)>
        {
            (new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31)),
            // Inclusive of both ends, so 31/10 collides with the window that ends on it.
            (new DateOnly(2025, 10, 31), new DateOnly(2025, 11, 30)),
        };

        var layout = RotationCyclePlanner.Build([Medecine, Chirurgie], 1, ["A", "B"], overlapping);

        layout.IsFailure.Should().BeTrue();
        layout.Error.Code.Should().Be("RotationCycle.WindowsOverlap");
    }

    [Fact]
    public void Windows_given_out_of_order_are_sorted_rather_than_refused()
    {
        var shuffled = Months(4).OrderByDescending(w => w.Item1).ToList();

        var layout = RotationCyclePlanner.Build([Medecine, Chirurgie], 2, ["A", "B"], shuffled);

        layout.IsSuccess.Should().BeTrue();
        layout.Value.Columns.Select(c => c.StartDate).Should().BeInAscendingOrder();
        layout.Value.Columns[0].PeriodNumber.Should().Be(1);
    }

    [Fact]
    public void A_stage_listed_twice_is_refused()
    {
        var layout = RotationCyclePlanner.Build(
            [Medecine, Medecine], 1, ["A", "B"], Months(2));

        layout.IsFailure.Should().BeTrue();
        layout.Error.Code.Should().Be("RotationCycle.DuplicateStage");
    }

    [Fact]
    public void A_promotion_with_no_partitions_yet_is_refused_by_name()
    {
        var layout = RotationCyclePlanner.Build([Medecine, Chirurgie], 2, [], Months(4));

        layout.IsFailure.Should().BeTrue();
        layout.Error.Code.Should().Be("RotationCycle.NoPartitions");
    }

    [Fact]
    public void A_single_stage_block_is_legal_and_puts_everyone_in_it()
    {
        // The degenerate case: one stage, no crossover to compute, and it must not throw.
        var layout = RotationCyclePlanner.Build([Medecine], 3, ["A", "B"], Months(3));

        layout.IsSuccess.Should().BeTrue();
        layout.Value.Columns.Should().HaveCount(3);
        layout.Value.Matrix.Should().HaveCount(2);
        layout.Value.Matrix.Should().AllSatisfy(m => m.PeriodNumbers.Should().BeEquivalentTo([1, 2, 3]));
    }

    [Fact]
    public void Turns_group_the_columns_a_partition_spends_in_one_stage()
    {
        var layout = RotationCyclePlanner.Build([Medecine, Chirurgie], 3, ["A", "B"], Months(6));

        layout.Value.Columns.Where(c => c.Turn == 0).Select(c => c.PeriodNumber)
            .Should().BeEquivalentTo([1, 2, 3]);
        layout.Value.Columns.Where(c => c.Turn == 1).Select(c => c.PeriodNumber)
            .Should().BeEquivalentTo([4, 5, 6]);

        Plan(layout.Value, "A", Medecine).Should().BeEquivalentTo([1, 2, 3]);
    }

    private static IReadOnlyList<int> Plan(RotationCycleLayout layout, string partition, int stageId) =>
        layout.Matrix.Single(m => m.RotationGroup == partition && m.StageId == stageId).PeriodNumbers;
}
