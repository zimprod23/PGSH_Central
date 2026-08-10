using FluentAssertions;
using PGSH.Application.Stages.RotationCycle;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Evidence that the planner is general rather than fitted to the three shapes the faculty happens to
/// use today. Nothing here names a semester, a stage count or a duration: blocks are generated, and every
/// answer is checked against the invariants that define a correct rotation.
/// </summary>
/// <remarks>
/// The seed is fixed, so a failure is reproducible. If one appears, print the offending block — the
/// counterexample is the whole value of a test like this.
/// </remarks>
public class RotationCycleGeneralityTests
{
    private static List<(DateOnly, DateOnly)> Months(int count) =>
        Enumerable.Range(0, count)
            .Select(i =>
            {
                var start = new DateOnly(2025, 9, 1).AddMonths(i);
                return (start, start.AddMonths(1).AddDays(-1));
            })
            .ToList();

    private static IReadOnlyList<string> Partitions(int count) =>
        Enumerable.Range(0, count).Select(i => $"P{i:D2}").ToList();

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }

    /// <summary>
    /// Every property that makes a rotation correct, asserted together. Any of them failing means the plan
    /// would put a student in two places, skip a stage, or over-fill a service.
    /// </summary>
    private static void AssertValid(
        RotationCycleLayout layout, IReadOnlyList<RotationStage> stages, IReadOnlyList<string> partitions)
    {
        int timeline = stages.Sum(s => s.Periods);
        layout.Timeline.Should().Be(timeline);

        foreach (string p in partitions)
        {
            var mine = layout.Matrix.Where(m => m.RotationGroup == p).ToList();

            mine.Select(m => m.StageId).Should()
                .BeEquivalentTo(stages.Select(s => s.StageId), $"{p} visits every stage exactly once");

            foreach (var m in mine)
                m.PeriodNumbers.Should().HaveCount(
                    stages.Single(x => x.StageId == m.StageId).Periods,
                    $"{p} stays in stage {m.StageId} for its own number of periods");

            // Contiguous: a student does not leave a stage and come back to it later.
            foreach (var m in mine)
                m.PeriodNumbers.Should().BeEquivalentTo(
                    Enumerable.Range(m.PeriodNumbers.Min(), m.PeriodNumbers.Count),
                    $"{p}'s time in stage {m.StageId} is one unbroken run");

            var occupied = mine.SelectMany(m => m.PeriodNumbers).ToList();
            occupied.Should().OnlyHaveUniqueItems($"{p} is never in two stages at once");
            occupied.Should().BeEquivalentTo(
                Enumerable.Range(1, timeline), $"{p} is somewhere in every column");
        }

        foreach (var t in layout.Stages)
        {
            for (int column = 1; column <= timeline; column++)
            {
                layout.Matrix.Count(m => m.StageId == t.StageId && m.PeriodNumbers.Contains(column))
                    .Should().Be(t.Concurrency,
                        $"stage {t.StageId} holds exactly Lₛ partitions in column {column}");
            }
        }
    }

    [Fact]
    public void Equal_durations_always_solve_for_any_shape_and_any_valid_partition_count()
    {
        // The family that is always feasible: S stages of k periods each. If the general solver ever
        // failed here it would have regressed the cases that used to work by closed form.
        // Capped at six stages: the widest real block. Seven means 5 040 schedules to enumerate and
        // pushed this one test past a minute, which is how a suite stops being run.
        for (int stageCount = 1; stageCount <= 6; stageCount++)
        {
            for (int periods = 1; periods <= 4; periods++)
            {
                var stages = Enumerable.Range(1, stageCount)
                    .Select(id => new RotationStage(id, periods))
                    .ToList();

                int timeline = stageCount * periods;
                int step = timeline / periods;   // gcd of equal durations is the duration itself

                foreach (int multiplier in new[] { 1, 2 })
                {
                    var partitions = Partitions(step * multiplier);
                    var layout = RotationCyclePlanner.Build(stages, partitions, Months(timeline));

                    layout.IsSuccess.Should().BeTrue(
                        $"{stageCount} stages × {periods} periods with {partitions.Count} partitions");
                    AssertValid(layout.Value, stages, partitions);
                }
            }
        }
    }

    [Fact]
    public void Generated_blocks_of_mixed_durations_are_either_solved_correctly_or_refused_for_a_named_reason()
    {
        var rng = new Random(20260809);
        int solved = 0, refused = 0;

        for (int trial = 0; trial < 150; trial++)
        {
            int stageCount = rng.Next(2, 6);
            var stages = Enumerable.Range(1, stageCount)
                .Select(id => new RotationStage(id, rng.Next(1, 4)))
                .ToList();

            int timeline = stages.Sum(s => s.Periods);
            int step = timeline / stages.Select(s => s.Periods).Aggregate(Gcd);
            var partitions = Partitions(step * rng.Next(1, 3));

            var layout = RotationCyclePlanner.Build(stages, partitions, Months(timeline));

            if (layout.IsSuccess)
            {
                solved++;
                AssertValid(layout.Value, stages, partitions);
                layout.Value.PartitionStep.Should().Be(step);
            }
            else
            {
                refused++;
                // The partition count was constructed to satisfy the arithmetic, so what is left is either
                // a proof that no arrangement exists or an honest "I did not finish deciding". Both are
                // named outcomes; what must never happen is a wrong plan or a hang.
                layout.Error.Code.Should().BeOneOf(
                    ["RotationCycle.NoFeasibleArrangement", "RotationCycle.ArrangementUndetermined"],
                    $"durations [{string.Join(",", stages.Select(s => s.Periods))}] "
                    + $"with {partitions.Count} partitions");
            }
        }

        // Both outcomes must actually occur, or the test is not exercising what it claims.
        solved.Should().BeGreaterThan(0);
        refused.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The shapes a faculty actually publishes must always resolve — this is the guarantee that matters,
    /// as distinct from the fuzz test above, which deliberately reaches past realistic sizes.
    /// </summary>
    /// <remarks>
    /// ⚠ The solver's budget is finite, so exotic blocks (five stages of mixed length with sixteen
    /// partitions, say) can return <c>ArrangementUndetermined</c>. That is honest rather than wrong, but it
    /// means "general" has a boundary, and this test is where the boundary is asserted to be beyond
    /// anything real.
    /// </remarks>
    [Theory]
    // (durations, partition count) — the real cases, plus headroom around them.
    [InlineData(new[] { 1, 1 }, 2)]
    [InlineData(new[] { 2, 2 }, 2)]
    [InlineData(new[] { 2, 2 }, 4)]
    [InlineData(new[] { 1, 1, 1 }, 3)]
    [InlineData(new[] { 1, 1, 1 }, 6)]
    [InlineData(new[] { 2, 2, 2 }, 3)]
    [InlineData(new[] { 1, 1, 1, 1 }, 4)]
    [InlineData(new[] { 1, 1, 1, 1, 1 }, 5)]
    [InlineData(new[] { 2, 2, 2, 2 }, 4)]
    [InlineData(new[] { 2, 2, 2, 2, 1, 1 }, 10)]   // the real 6th year
    [InlineData(new[] { 2, 2, 1, 1 }, 6)]
    [InlineData(new[] { 2, 1, 1 }, 4)]
    [InlineData(new[] { 1, 1, 1, 1, 1, 1 }, 6)]
    public void Every_shape_the_faculty_actually_uses_resolves(int[] durations, int partitionCount)
    {
        var stages = durations.Select((k, i) => new RotationStage(i + 1, k)).ToList();
        var partitions = Partitions(partitionCount);

        var layout = RotationCyclePlanner.Build(stages, partitions, Months(durations.Sum()));

        layout.IsSuccess.Should().BeTrue(
            $"durations [{string.Join(",", durations)}] with {partitionCount} partitions "
            + $"— got {(layout.IsFailure ? layout.Error.Code : "success")}");

        AssertValid(layout.Value, stages, partitions);
    }

    [Fact]
    public void A_partition_count_off_the_required_multiple_is_always_refused_on_the_arithmetic()
    {
        var rng = new Random(1650);

        for (int trial = 0; trial < 120; trial++)
        {
            var stages = Enumerable.Range(1, rng.Next(2, 6))
                .Select(id => new RotationStage(id, rng.Next(1, 4)))
                .ToList();

            int timeline = stages.Sum(s => s.Periods);
            int step = timeline / stages.Select(s => s.Periods).Aggregate(Gcd);
            if (step == 1) continue;   // every count is a multiple of 1

            var layout = RotationCyclePlanner.Build(stages, Partitions(step + 1), Months(timeline));

            layout.IsFailure.Should().BeTrue();
            layout.Error.Code.Should().Be("RotationCycle.PartitionCountIncompatible");
            layout.Error.Description.Should().Contain(step.ToString());
        }
    }

    [Fact]
    public void The_same_block_always_produces_the_same_plan()
    {
        var stages = new List<RotationStage> { new(1, 2), new(2, 2), new(3, 1), new(4, 1) };
        var partitions = Partitions(6);

        var first = RotationCyclePlanner.Build(stages, partitions, Months(6));
        var second = RotationCyclePlanner.Build(stages, partitions, Months(6));

        // A published répartition must not change shape when the plan is regenerated.
        first.Value.Matrix.Should().BeEquivalentTo(second.Value.Matrix, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Stage_order_chooses_the_rotation_without_changing_its_validity()
    {
        var forward = new List<RotationStage> { new(1, 2), new(2, 1), new(3, 1), new(4, 2) };
        var reversed = forward.AsEnumerable().Reverse().ToList();
        var partitions = Partitions(6);

        var a = RotationCyclePlanner.Build(forward, partitions, Months(6));
        var b = RotationCyclePlanner.Build(reversed, partitions, Months(6));

        a.IsSuccess.Should().BeTrue();
        b.IsSuccess.Should().BeTrue();

        // Both are correct rotations; which stage a partition opens on is the caller's choice.
        AssertValid(a.Value, forward, partitions);
        AssertValid(b.Value, reversed, partitions);
    }
}
