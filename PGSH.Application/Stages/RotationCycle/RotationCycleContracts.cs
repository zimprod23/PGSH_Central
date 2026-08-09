using PGSH.Application.Stages.MacroPlan;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// One column of the block's shared axis. Every stage of the block declares a slot on <em>this</em>
/// window, which is what makes the columns line up by construction instead of by everyone typing the
/// same dates.
/// </summary>
/// <param name="Turn">
/// Which stage-turn the column belongs to: with <c>periodsPerStage = 2</c>, columns 1–2 are turn 0 and
/// columns 3–4 are turn 1. A partition stays in one stage for a whole turn.
/// </param>
public sealed record RotationColumn(int PeriodNumber, int Turn, DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// Which partitions share a place in the rotation. With as many partitions as stages every lane holds
/// one; with twice as many, each lane holds two and they move through the stages together.
/// </summary>
public sealed record RotationLane(int Index, IReadOnlyList<string> Partitions);

/// <summary>
/// The whole cycle, worked out but not yet written. <see cref="Matrix"/> is deliberately the type
/// <c>GenerateMacroPlanCommand</c> already consumes: this feature generates the matrix that was
/// previously ticked by hand, and changes nothing downstream of it.
/// </summary>
public sealed record RotationCycleLayout(
    int StagesInBlock,
    int PeriodsPerStage,
    IReadOnlyList<RotationColumn> Columns,
    IReadOnlyList<RotationLane> Lanes,
    IReadOnlyList<PartitionStagePlan> Matrix,
    // Things that are legal but probably not intended — uneven lanes, mostly. Never blocking.
    IReadOnlyList<string> Warnings);
