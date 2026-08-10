using FluentAssertions;
using PGSH.Application.Stages.RotationCycle;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The crossover arithmetic, tested without a database because it needs none. A block occupies
/// <c>T = Σkₛ</c> columns; stage <c>s</c> is cut into <c>T/kₛ</c> slots each holding <c>P·kₛ/T</c>
/// partitions; and every partition must tile the whole timeline with exactly one slot of every stage.
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
        layout.Value.Timeline.Should().Be(4);

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
        // Six partitions over three one-column stages: two partitions share every slot.
        layout.Value.Stages.Should().AllSatisfy(t => t.Concurrency.Should().Be(2));
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
    [InlineData(2, 2, 4)]
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

        // The property GroupScheduleConflictGuard would otherwise have to catch after the fact. Period
        // numbers are per-stage slot indices now, so the columns have to be reconstructed from the slots.
        foreach (string partition in partitions)
        {
            var occupied = layout.Value.Matrix
                .Where(m => m.RotationGroup == partition)
                .SelectMany(m => m.PeriodNumbers)
                .ToList();

            occupied.Should().OnlyHaveUniqueItems($"{partition} is in one place per column");
            occupied.Should().BeEquivalentTo(
                layout.Value.Columns.Select(c => c.Number),
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
                .Distinct()
                .ToList();

            // A stage holds someone in every column — that is what makes the published table full.
            covered.Should().BeEquivalentTo(layout.Value.Columns.Select(c => c.Number));
        }
    }

    [Fact]
    public void A_partition_count_that_does_not_divide_the_block_is_refused_with_the_multiples_that_would()
    {
        var layout = RotationCyclePlanner.Build(
            [Medecine, Chirurgie, Pediatrie], 1, ["A", "B", "C", "D"], Months(3));

        // 4 partitions over a 3-column block: concurrency would be 4/3 of a partition per stage.
        layout.IsFailure.Should().BeTrue();
        layout.Error.Code.Should().Be("RotationCycle.PartitionCountIncompatible");
    }

    [Fact]
    public void Fewer_partitions_than_stages_is_refused_rather_than_leaving_a_stage_empty()
    {
        var layout = RotationCyclePlanner.Build(
            [Medecine, Chirurgie, Pediatrie], 1, ["A", "B"], Months(3));

        // Two partitions cannot occupy three concurrent stages: one would stand empty every column.
        layout.IsFailure.Should().BeTrue();
        layout.Error.Code.Should().Be("RotationCycle.PartitionCountIncompatible");
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
        layout.Value.Columns[0].Number.Should().Be(1);
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
        // Three columns in one stage: the partition passes through three services, P1-P3.
        layout.Value.Matrix.Should().AllSatisfy(m => m.PeriodNumbers.Should().BeEquivalentTo([1, 2, 3]));
    }

    [Fact]
    public void A_multi_period_stage_gives_a_partition_a_run_of_consecutive_services()
    {
        var layout = RotationCyclePlanner.Build([Medecine, Chirurgie], 3, ["A", "B"], Months(6));

        // Both stages span the whole six-column axis; a partition takes a run of three in each. Three
        // periods means three *different services*, not one service held for three months.
        layout.Value.Stages.Should().AllSatisfy(t => t.SlotCount.Should().Be(6));
        Plan(layout.Value, "A", Medecine).Should().BeEquivalentTo([1, 2, 3]);
        Plan(layout.Value, "A", Chirurgie).Should().BeEquivalentTo([4, 5, 6]);
    }

    // ── The cases from the faculty's own planning ────────────────────────────────────────────────

    [Fact]
    public void The_new_third_year_runs_as_two_semester_blocks_of_three()
    {
        // Semester 1: pédiatrie, cardio, dermato — one period each, three partitions, three columns.
        var s1 = RotationCyclePlanner.Build([1, 2, 3], 1, ["A", "B", "C"], Months(3));

        s1.IsSuccess.Should().BeTrue();
        s1.Value.Timeline.Should().Be(3);
        s1.Value.Stages.Should().AllSatisfy(t => t.Concurrency.Should().Be(1));

        foreach (string p in new[] { "A", "B", "C" })
            s1.Value.Matrix.Where(m => m.RotationGroup == p).Select(m => m.StageId)
                .Should().BeEquivalentTo([1, 2, 3], $"{p} does all three stages of the semester");
    }

    [Fact]
    public void The_sixth_year_mixes_two_period_and_one_period_stages_on_one_axis()
    {
        // Four stages of 2 periods and two of 1 → T = 10 monthly columns, exactly Med6.png.
        var stages = new List<RotationStage>
        {
            new(1, 2), new(2, 2), new(3, 2), new(4, 2), new(5, 1), new(6, 1),
        };
        var partitions = Enumerable.Range(0, 10).Select(i => ((char)('A' + i)).ToString()).ToList();

        var layout = RotationCyclePlanner.Build(stages, partitions, Months(10));

        layout.IsSuccess.Should().BeTrue();
        layout.Value.Timeline.Should().Be(10);

        // Lₛ = P·kₛ/T — two partitions at a time in each two-period stage, one in each one-period stage,
        // summing to the ten partitions.
        layout.Value.Stages.Where(t => t.Periods == 2).Should().AllSatisfy(t => t.Concurrency.Should().Be(2));
        layout.Value.Stages.Where(t => t.Periods == 1).Should().AllSatisfy(t => t.Concurrency.Should().Be(1));
        layout.Value.Stages.Sum(t => t.Concurrency).Should().Be(10);

        foreach (string p in partitions)
        {
            var mine = layout.Value.Matrix.Where(m => m.RotationGroup == p).ToList();

            mine.Select(m => m.StageId).Should().BeEquivalentTo([1, 2, 3, 4, 5, 6]);
            foreach (var m in mine)
                m.PeriodNumbers.Should().HaveCount(stages.Single(x => x.StageId == m.StageId).Periods);

            var occupied = mine.SelectMany(m => m.PeriodNumbers).ToList();
            occupied.Should().OnlyHaveUniqueItems($"{p} is in one place per column");
            occupied.Should().BeEquivalentTo(Enumerable.Range(1, 10), $"{p} fills the whole year");
        }

        // And no stage is ever over or under its concurrency in any column.
        foreach (var t in layout.Value.Stages)
        {
            for (int column = 1; column <= 10; column++)
            {
                layout.Value.Matrix
                    .Count(m => m.StageId == t.StageId && m.PeriodNumbers.Contains(column))
                    .Should().Be(t.Concurrency, $"stage {t.StageId} in column {column}");
            }
        }
    }

    [Fact]
    public void A_two_period_stage_beside_a_one_period_stage_is_impossible_and_says_so()
    {
        // T = 3, and a two-column run must cover column 2 wherever it starts — so every partition is in
        // that stage there and the other stands empty. No partition count rescues it.
        var layout = RotationCyclePlanner.Build(
            [new RotationStage(1, 2), new RotationStage(2, 1)], ["A", "B", "C"], Months(3));

        layout.IsFailure.Should().BeTrue();
        layout.Error.Code.Should().Be("RotationCycle.NoFeasibleArrangement");
    }

    [Fact]
    public void Mixed_durations_pin_the_partition_count_and_the_message_names_the_multiples()
    {
        // T = 6, gcd(kₛ) = 1, so P must be a multiple of 6. Four partitions cannot work.
        var refused = RotationCyclePlanner.Build(
            [new RotationStage(1, 2), new RotationStage(2, 2), new RotationStage(3, 1), new RotationStage(4, 1)],
            ["A", "B", "C", "D"], Months(6));

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("RotationCycle.PartitionCountIncompatible");
        refused.Error.Description.Should().Contain("6");
    }

    [Fact]
    public void Five_stages_across_the_year_with_five_partitions()
    {
        var layout = RotationCyclePlanner.Build(
            [1, 2, 3, 4, 5], 1, ["A", "B", "C", "D", "E"], Months(5));

        layout.IsSuccess.Should().BeTrue();
        layout.Value.Timeline.Should().Be(5);
        layout.Value.Stages.Should().AllSatisfy(t => t.Concurrency.Should().Be(1));
    }

    private static IReadOnlyList<int> Plan(RotationCycleLayout layout, string partition, int stageId) =>
        layout.Matrix.Single(m => m.RotationGroup == partition && m.StageId == stageId).PeriodNumbers;
}
