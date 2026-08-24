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

/// <summary>Why <see cref="RotationTiling.Solve"/> returned nothing.</summary>
public enum TilingOutcome
{
    Solved,

    /// <summary>The search completed and found nothing — no arrangement exists.</summary>
    ProvenImpossible,

    /// <summary>
    /// The search was still running when it hit its budget. Distinct from
    /// <see cref="ProvenImpossible"/> on purpose: claiming a proof we did not finish would be a lie.
    /// </summary>
    Undetermined,
}

public sealed record TilingResult(
    TilingOutcome Outcome,
    IReadOnlyList<IReadOnlyList<PartitionPlacement>> Plan);

/// <summary>
/// Solves the arrangement a rotation block needs: give every partition one contiguous run of columns in
/// every stage, such that those runs tile the whole timeline and each stage holds exactly
/// <see cref="StageTiling.Concurrency"/> partitions in every column.
///
/// <para><b>Partitions are interchangeable, and that is the whole algorithm.</b> A partition's year is an
/// ordering of the block's stages laid end to end, so there are at most <c>S!</c> distinct schedules. What
/// has to be decided is not <em>which</em> partition takes which schedule but <em>how many</em> take each
/// — so the search runs over multiplicities of schedules, not over assignments of partitions.</para>
///
/// <para>⚠ That reformulation is not an optimisation, it is what makes the search terminate. Assigning
/// partitions one at a time explores <c>(S!)ᴾ</c> orderings — seven stages and fourteen partitions
/// crashed the test host outright. Over multiplicities the space is bounded by the capacity constraints
/// instead, and realistic blocks resolve immediately.</para>
///
/// <para>A stage is occupied in every column, so the capacity being consumed is per (stage, column), and a
/// run of <c>kₛ</c> columns may begin anywhere — it is a run, not a slot on a coarser grid. That is what
/// lets a two-period stage and a one-period stage share one monthly axis.</para>
/// </summary>
internal static class RotationTiling
{
    /// <summary>
    /// Bounds the work so a planning request cannot hang. Generous next to any real block: the faculty's
    /// widest is six stages over ten columns, which settles in a few hundred steps.
    /// </summary>
    private const int NodeBudget = 2_000_000;

    /// <summary>
    /// Deterministic: schedules are enumerated in a fixed order and taken lowest-index first, so the same
    /// block always produces the same plan. A published répartition must not change shape on a re-run.
    ///
    /// <para><b>The caller's stage order is the first partition's year.</b> <see cref="Enumerate"/> walks
    /// the stages in the order they were given, so <c>schedules[0]</c> is that order laid end to end, and
    /// the first partition takes the lowest-index schedule that fits. Entering Gynéco(3), Neuro, ORL…
    /// therefore gives partition A columns 1-3 in Gynéco, 4 in Neuro, 5 in ORL — which is what the
    /// authored order means, and what the reopened form has to show back. Preferred, not guaranteed:
    /// where no complete arrangement contains it, a later schedule wins over failing.</para>
    /// </summary>
    public static TilingResult Solve(
        IReadOnlyList<StageTiling> stages,
        int partitionCount,
        int timeline)
    {
        var schedules = Enumerate(stages, timeline);
        if (schedules.Count == 0)
            return new TilingResult(TilingOutcome.ProvenImpossible, []);

        // demand[s][t] — partitions still needed in stage s at column t.
        var demand = stages
            .Select(s => Enumerable.Repeat(s.Concurrency, timeline).ToArray())
            .ToArray();

        var chosen = new int[partitionCount];
        int budget = NodeBudget;

        bool solved = Choose(schedules, demand, chosen, depth: 0, startIndex: 0, ref budget);

        if (solved)
        {
            var plan = chosen.Select(i => schedules[i].Placements).ToList();
            return new TilingResult(TilingOutcome.Solved, plan);
        }

        return new TilingResult(
            budget > 0 ? TilingOutcome.ProvenImpossible : TilingOutcome.Undetermined, []);
    }

    /// <summary>One possible year for a partition: an ordering of the stages, with the columns each takes.</summary>
    private sealed record Schedule(
        IReadOnlyList<PartitionPlacement> Placements,
        int[][] Occupies);

    /// <summary>
    /// Every distinct schedule, built by permuting the stages and laying them end to end. Two permutations
    /// that occupy the same (stage, column) cells are the same schedule and are kept once.
    /// </summary>
    private static List<Schedule> Enumerate(IReadOnlyList<StageTiling> stages, int timeline)
    {
        var schedules = new List<Schedule>();
        var seen = new HashSet<string>();
        var order = new int[stages.Count];
        var used = new bool[stages.Count];

        void Walk(int depth, int column)
        {
            if (depth == stages.Count)
            {
                if (column != timeline) return;

                var placements = new List<PartitionPlacement>(stages.Count);
                var occupies = stages.Select(_ => new int[timeline]).ToArray();
                int c = 0;

                foreach (int s in order)
                {
                    placements.Add(new PartitionPlacement(stages[s].StageId, c + 1, c + stages[s].Periods));
                    for (int t = c; t < c + stages[s].Periods; t++) occupies[s][t] = 1;
                    c += stages[s].Periods;
                }

                string key = string.Join("|", occupies.Select(row => string.Concat(row)));
                if (seen.Add(key))
                    schedules.Add(new Schedule(placements, occupies));

                return;
            }

            for (int s = 0; s < stages.Count; s++)
            {
                if (used[s] || column + stages[s].Periods > timeline) continue;

                used[s] = true;
                order[depth] = s;
                Walk(depth + 1, column + stages[s].Periods);
                used[s] = false;
            }
        }

        Walk(0, 0);
        return schedules;
    }

    /// <summary>
    /// Hands one schedule to partition <paramref name="depth"/>, then the partitions after it.
    /// </summary>
    /// <remarks>
    /// ⚠ Recursion depth is the <b>partition count</b>, never the schedule count. Recursing once per
    /// schedule overflowed the stack for seven stages — 7! = 5040 frames — which kills the process
    /// uncatchably rather than failing a request. Partitions number in the tens; schedules can number
    /// thousands.
    ///
    /// <para><paramref name="startIndex"/> is what keeps this from re-exploring the same multiset in every
    /// order: partitions are interchangeable, so schedules are taken in non-decreasing index order.</para>
    /// </remarks>
    private static bool Choose(
        List<Schedule> schedules,
        int[][] demand,
        int[] chosen,
        int depth,
        int startIndex,
        ref int budget)
    {
        if (--budget <= 0) return false;

        if (depth == chosen.Length)
            return demand.All(row => row.All(v => v == 0));

        int remaining = chosen.Length - depth;

        // Exact prune: a schedule covers exactly one column in each of its stages, so it consumes exactly
        // `timeline` cells. What is left must divide among the partitions left, whatever the choices.
        int outstanding = 0;
        foreach (var row in demand) foreach (int v in row) outstanding += v;
        if (outstanding != remaining * demand[0].Length) return false;

        for (int i = startIndex; i < schedules.Count; i++)
        {
            if (--budget <= 0) return false;

            var schedule = schedules[i];

            bool fits = true;
            for (int s = 0; s < demand.Length && fits; s++)
                for (int t = 0; t < demand[s].Length && fits; t++)
                    if (schedule.Occupies[s][t] == 1 && demand[s][t] == 0)
                        fits = false;

            if (!fits) continue;

            Apply(demand, schedule, -1);
            chosen[depth] = i;

            if (Choose(schedules, demand, chosen, depth + 1, i, ref budget))
            {
                Apply(demand, schedule, 1);
                return true;
            }

            Apply(demand, schedule, 1);
        }

        return false;
    }

    private static void Apply(int[][] demand, Schedule schedule, int delta)
    {
        for (int s = 0; s < demand.Length; s++)
            for (int t = 0; t < demand[s].Length; t++)
                if (schedule.Occupies[s][t] == 1)
                    demand[s][t] += delta;
    }
}
