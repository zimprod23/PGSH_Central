using PGSH.Application.Stages.MacroPlan;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// Works out the crossover for a block of stages that run concurrently on one shared axis: which
/// partition sits in which stage, in which of that stage's periods, so that every partition passes
/// through every stage of the block exactly once and no stage is ever over- or under-filled.
///
/// <para>Pure — no database, no clock — which is what lets the arithmetic be tested exhaustively rather
/// than through a planning fixture.</para>
///
/// <para><b>The model.</b> Stage <c>s</c> holds a partition for <c>kₛ</c> columns, so a partition needs
/// <c>T = Σkₛ</c> columns to visit them all. If <c>Lₛ</c> partitions sit in stage <c>s</c> at any moment
/// then, counting partition-columns two ways, <c>Lₛ·T = P·kₛ</c>. Hence:</para>
/// <para><c>Lₛ = P·kₛ/T</c> must be a whole number, which pins <c>P</c> to a multiple of
/// <c>T / gcd(kₛ)</c>. That is the only arithmetic condition; the rest is arrangement.</para>
///
/// <para>⚠ <b>A period is one service, not one stage.</b> "Chirurgie has 2 periods" means a partition
/// spends two columns there and passes through <em>two different services</em> — so every stage carries a
/// slot per column of the axis, and a partition takes a run of <c>kₛ</c> consecutive ones. Modelling a
/// two-period stage as a single two-column slot would keep the group in one service for two months, which
/// is the opposite of what it means.</para>
///
/// <para><b>Why the old formula had to go.</b> With every <c>kₛ</c> equal this collapses to the cyclic
/// Latin square — partition <c>p</c> in stage <c>(p + t) mod S</c> — which is all the first version could
/// express. Unequal durations break it outright: stage boundaries of different lengths no longer line up,
/// so shifting a partition by one stage does not carry a valid schedule to another valid one. The
/// arrangement is now solved as an exact cover (<see cref="RotationTiling"/>).</para>
///
/// <para>⚠ <b>Some duration mixes are impossible, not merely unsupported.</b> Stages of 2 and 1 columns
/// give <c>T = 3</c>, and a two-column run in a three-column timeline must cover column 2 wherever it
/// starts — so every partition is in that stage at column 2 and the other stands empty. No <c>P</c> fixes
/// it, and the search says so exhaustively rather than guessing.</para>
/// </summary>
internal static class RotationCyclePlanner
{
    public static Result<RotationCycleLayout> Build(
        IReadOnlyList<RotationStage> stages,
        IReadOnlyList<string> partitionLabels,
        IReadOnlyList<(DateOnly Start, DateOnly End)> windows)
    {
        if (stages.Count == 0)
            return Result.Failure<RotationCycleLayout>(RotationCycleErrors.NoStages);

        if (stages.Select(s => s.StageId).Distinct().Count() != stages.Count)
            return Result.Failure<RotationCycleLayout>(RotationCycleErrors.DuplicateStage);

        if (stages.Any(s => s.Periods < 1))
            return Result.Failure<RotationCycleLayout>(RotationCycleErrors.InvalidPeriods);

        var labels = partitionLabels.Distinct().OrderBy(l => l).ToList();
        if (labels.Count == 0)
            return Result.Failure<RotationCycleLayout>(RotationCycleErrors.NoPartitions);

        int timeline = stages.Sum(s => s.Periods);

        if (windows.Count != timeline)
            return Result.Failure<RotationCycleLayout>(
                RotationCycleErrors.WrongWindowCount(timeline, windows.Count, stages));

        // Concurrency must come out whole, which pins P to a multiple of T / gcd(kₛ).
        int divisor = stages.Select(s => s.Periods).Aggregate(Gcd);
        int step = timeline / divisor;

        if (labels.Count % step != 0)
            return Result.Failure<RotationCycleLayout>(
                RotationCycleErrors.PartitionCountIncompatible(labels.Count, step, timeline));

        var ordered = windows.OrderBy(w => w.Start).ThenBy(w => w.End).ToList();

        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start <= ordered[i - 1].End)
                return Result.Failure<RotationCycleLayout>(
                    RotationCycleErrors.WindowsOverlap(i, i + 1));
        }

        var columns = ordered
            .Select((w, i) => new RotationColumn(i + 1, w.Start, w.End))
            .ToList();

        // Every stage is occupied in every column, so every stage carries the whole axis as slots.
        var tilings = stages
            .Select(s => new StageTiling(
                s.StageId,
                s.Periods,
                SlotCount: timeline,
                Concurrency: labels.Count * s.Periods / timeline))
            .ToList();

        var arrangement = RotationTiling.Solve(tilings, labels.Count, timeline);
        if (arrangement is null)
            return Result.Failure<RotationCycleLayout>(
                RotationCycleErrors.NoFeasibleArrangement(labels.Count, timeline));

        // One StageSlot per (stage, column). Dated from the single list of windows, so Médecine's P1 and
        // Chirurgie's P1 cannot drift apart by a typo — they are the same dates, written once.
        var slots = tilings
            .SelectMany(t => columns.Select(c => new RotationSlot(
                t.StageId, c.Number, c.Number, c.Number, c.StartDate, c.EndDate)))
            .ToList();

        // A partition takes a run of kₛ consecutive periods of each stage — one service per period.
        var matrix = labels
            .SelectMany((label, p) => arrangement[p]
                .OrderBy(x => x.FirstColumn)
                .Select(x => new PartitionStagePlan(
                    label,
                    x.StageId,
                    Enumerable.Range(x.FirstColumn, x.LastColumn - x.FirstColumn + 1).ToList())))
            .ToList();

        return new RotationCycleLayout(
            timeline, columns, tilings, slots, matrix, Warnings(tilings, labels.Count, step));
    }

    /// <summary>Uniform durations — the common case, and what the two-stage mirror is.</summary>
    public static Result<RotationCycleLayout> Build(
        IReadOnlyList<int> stageIds,
        int periodsPerStage,
        IReadOnlyList<string> partitionLabels,
        IReadOnlyList<(DateOnly Start, DateOnly End)> windows) =>
        Build(stageIds.Select(id => new RotationStage(id, periodsPerStage)).ToList(), partitionLabels, windows);

    private static List<string> Warnings(
        IReadOnlyList<StageTiling> tilings, int partitionCount, int step)
    {
        var warnings = new List<string>();

        // Legal and correct, but it multiplies how many groups one stage holds at once, which is a
        // capacity question rather than a planning one.
        var shared = tilings.Where(t => t.Concurrency > 1).ToList();
        if (shared.Count > 0)
            warnings.Add(
                string.Join(" · ", shared.Select(t => $"{t.Concurrency} partitions à la fois en stage {t.StageId}"))
                + ". Vérifiez la capacité des services concernés.");

        if (partitionCount > step)
            warnings.Add(
                $"Le minimum de partitions pour ce bloc est {step}. Avec {partitionCount}, chaque créneau "
                + "accueille plusieurs partitions simultanément.");

        return warnings;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
