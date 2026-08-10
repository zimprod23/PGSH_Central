namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// One stage's place on the block's shared axis.
/// </summary>
/// <param name="Periods">
/// How many columns a partition spends here — and therefore how many <em>different services</em> it
/// passes through, since a column is one service placement.
/// </param>
/// <param name="SlotCount">
/// Always the whole timeline: the stage is occupied in every column, by different partitions. A partition
/// takes a run of <paramref name="Periods"/> consecutive slots.
/// </param>
/// <param name="Concurrency">How many partitions are here at once — <c>P·kₛ/T</c>.</param>
public sealed record StageTiling(int StageId, int Periods, int SlotCount, int Concurrency);

/// <summary>
/// The run of columns one partition spends in one stage. 1-based and inclusive, so a partition doing a
/// two-period stage over the first two months has <c>FirstColumn 1, LastColumn 2</c> — and therefore
/// takes that stage's periods 1 and 2, in two different services.
/// </summary>
public sealed record PartitionPlacement(int StageId, int FirstColumn, int LastColumn);

/// <summary>
/// Solves the arrangement a rotation block needs: give every partition exactly one slot of every stage,
/// such that those slots tile the whole timeline without overlap, and no slot holds more partitions than
/// its concurrency allows.
///
/// <para><b>Why this is a search and not a formula.</b> With equal durations the answer is the cyclic
/// Latin square — partition <c>p</c> in stage <c>(p + t) mod S</c> — and that is all the previous version
/// could express. Unequal durations break it: the stage boundaries of different lengths no longer line
/// up, so shifting a partition by one stage does not map one valid schedule onto another. What is left is
/// an exact-cover problem, small enough (a level has under a dozen columns and under ten stages) to solve
/// exactly rather than approximate.</para>
///
/// <para><b>A stage is occupied in every column</b>, by <c>Lₛ = P·kₛ/T</c> partitions at a time. So the
/// capacity being consumed is per (stage, column), and a partition's run of <c>kₛ</c> columns may start
/// anywhere — it is a run, not a slot on a coarser grid. That is what lets a two-period stage and a
/// one-period stage share one monthly axis.</para>
/// </summary>
internal static class RotationTiling
{
    /// <summary>
    /// Deterministic: stages are tried in the given order and partitions filled in turn, so the same
    /// input always produces the same plan. A published répartition must not change shape on a re-run.
    /// </summary>
    public static List<List<PartitionPlacement>>? Solve(
        IReadOnlyList<StageTiling> stages,
        int partitionCount,
        int timeline)
    {
        // remaining[s][t] — how many more partitions stage s can still take in column t.
        var remaining = stages
            .Select(s => Enumerable.Repeat(s.Concurrency, timeline).ToArray())
            .ToArray();

        var plan = Enumerable.Range(0, partitionCount)
            .Select(_ => new List<PartitionPlacement>(stages.Count))
            .ToList();

        return Fill(stages, remaining, timeline, plan, partition: 0, column: 0, used: new bool[stages.Count])
            ? plan
            : null;
    }

    /// <summary>
    /// Lays out partition <paramref name="partition"/> from <paramref name="column"/> onward, then the
    /// partitions after it.
    /// </summary>
    /// <remarks>
    /// One recursion across both dimensions on purpose. Filling each partition greedily and moving on
    /// would report "impossible" whenever an early partition took a slot a later one needed, which is a
    /// wrong answer rather than a slow one — and "no arrangement exists" is a claim this has to be able
    /// to make honestly, since for some duration mixes it is the truth.
    /// </remarks>
    private static bool Fill(
        IReadOnlyList<StageTiling> stages,
        int[][] remaining,
        int timeline,
        List<List<PartitionPlacement>> plan,
        int partition,
        int column,
        bool[] used)
    {
        if (column == timeline)
        {
            if (!used.All(u => u)) return false;
            if (partition == plan.Count - 1) return true;

            // This partition's year is complete and valid; start the next one.
            return Fill(stages, remaining, timeline, plan, partition + 1, 0, new bool[stages.Count]);
        }

        for (int s = 0; s < stages.Count; s++)
        {
            if (used[s]) continue;

            var stage = stages[s];

            if (column + stage.Periods > timeline) continue;

            // The run has to have room in every column it covers.
            bool fits = true;
            for (int t = column; t < column + stage.Periods && fits; t++)
                fits = remaining[s][t] > 0;
            if (!fits) continue;

            used[s] = true;
            for (int t = column; t < column + stage.Periods; t++) remaining[s][t]--;
            plan[partition].Add(new PartitionPlacement(
                stage.StageId, column + 1, column + stage.Periods));

            if (Fill(stages, remaining, timeline, plan, partition, column + stage.Periods, used))
                return true;

            plan[partition].RemoveAt(plan[partition].Count - 1);
            for (int t = column; t < column + stage.Periods; t++) remaining[s][t]++;
            used[s] = false;
        }

        return false;
    }
}
