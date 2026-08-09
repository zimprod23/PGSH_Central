using PGSH.Application.Stages.MacroPlan;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// Works out the crossover for a block of stages that run concurrently: which partition is in which
/// stage, in which periods, so that every partition passes through every stage of the block exactly
/// once and no two partitions crowd the same services at the same time.
///
/// <para>Pure — no database, no clock. Everything it needs is in the arguments, which is what lets the
/// rule be tested exhaustively rather than through a planning fixture.</para>
///
/// <para><b>The rule.</b> A block of <c>S</c> stages, each taking <c>k</c> periods, occupies
/// <c>S × k</c> columns: the timeline must be long enough for one partition to visit all <c>S</c>
/// stages. ⚠ It is <b>not</b> partitions × k — partitions do not lengthen the timeline, they subdivide
/// who is where. Partition <c>p</c> takes lane <c>p mod S</c> and, in turn <c>t</c>, sits in stage
/// <c>(lane + t) mod S</c>. With S = 2 that is exactly the mirror: A does stage 1 then stage 2, B does
/// stage 2 then stage 1.</para>
/// </summary>
internal static class RotationCyclePlanner
{
    public static Result<RotationCycleLayout> Build(
        IReadOnlyList<int> stageIds,
        int periodsPerStage,
        IReadOnlyList<string> partitionLabels,
        IReadOnlyList<(DateOnly Start, DateOnly End)> windows)
    {
        if (stageIds.Count == 0)
            return Result.Failure<RotationCycleLayout>(RotationCycleErrors.NoStages);

        if (stageIds.Distinct().Count() != stageIds.Count)
            return Result.Failure<RotationCycleLayout>(RotationCycleErrors.DuplicateStage);

        if (partitionLabels.Count == 0)
            return Result.Failure<RotationCycleLayout>(RotationCycleErrors.NoPartitions);

        if (periodsPerStage < 1)
            return Result.Failure<RotationCycleLayout>(
                Error.Validation("RotationCycle.InvalidPeriodsPerStage",
                    "Un stage occupe au moins une période."));

        int stages = stageIds.Count;
        int expectedColumns = stages * periodsPerStage;

        if (windows.Count != expectedColumns)
            return Result.Failure<RotationCycleLayout>(
                RotationCycleErrors.WrongWindowCount(expectedColumns, windows.Count));

        var ordered = windows.OrderBy(w => w.Start).ThenBy(w => w.End).ToList();

        // Windows are inclusive of both ends, the same convention SlotOverlapGuard enforces: a period
        // ending 31/03 and one starting 31/03 collide, and the next must start 01/04.
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start <= ordered[i - 1].End)
                return Result.Failure<RotationCycleLayout>(
                    RotationCycleErrors.WindowsOverlap(i, i + 1));
        }

        var columns = ordered
            .Select((w, i) => new RotationColumn(i + 1, i / periodsPerStage, w.Start, w.End))
            .ToList();

        var labels = partitionLabels.Distinct().OrderBy(l => l).ToList();

        var lanes = Enumerable.Range(0, stages)
            .Select(lane => new RotationLane(
                lane,
                labels.Where((_, pi) => pi % stages == lane).ToList()))
            .ToList();

        var matrix = new List<PartitionStagePlan>(labels.Count * stages);

        foreach (var lane in lanes)
        {
            foreach (string label in lane.Partitions)
            {
                for (int turn = 0; turn < stages; turn++)
                {
                    int stageIndex = (lane.Index + turn) % stages;

                    var periodNumbers = columns
                        .Where(c => c.Turn == turn)
                        .Select(c => c.PeriodNumber)
                        .ToList();

                    matrix.Add(new PartitionStagePlan(label, stageIds[stageIndex], periodNumbers));
                }
            }
        }

        return new RotationCycleLayout(
            stages, periodsPerStage, columns, lanes,
            matrix.OrderBy(m => m.RotationGroup).ThenBy(m => m.PeriodNumbers[0]).ToList(),
            Warnings(lanes, labels.Count, stages));
    }

    private static List<string> Warnings(
        IReadOnlyList<RotationLane> lanes, int partitionCount, int stages)
    {
        var warnings = new List<string>();

        // Legal, and the plan is still correct — every partition visits every stage. But the stages of a
        // turn then carry unequal shares of the promotion, which is a capacity surprise if unintended.
        //
        // Only worth saying when there are enough partitions to go round: below that, every non-empty
        // lane is trivially "heavier" than the empty ones, and the shortage warning says it properly.
        if (partitionCount > stages && partitionCount % stages != 0)
        {
            var heavy = lanes.Where(l => l.Partitions.Count > partitionCount / stages).ToList();
            warnings.Add(
                $"{partitionCount} partitions pour {stages} stages : les couloirs "
                + $"{string.Join(", ", heavy.Select(l => l.Index + 1))} portent une partition de plus. "
                + "Chaque partition passe bien par chaque stage, mais les stages d'un même tour "
                + "n'accueillent pas le même effectif.");
        }

        var empty = lanes.Where(l => l.Partitions.Count == 0).ToList();
        if (empty.Count > 0)
            warnings.Add(
                $"{empty.Count} couloir(s) sans partition : il y a moins de partitions que de stages, "
                + "donc certains stages n'accueillent personne pendant un tour entier.");

        return warnings;
    }
}
