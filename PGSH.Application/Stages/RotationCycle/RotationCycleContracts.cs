using PGSH.Application.Stages.MacroPlan;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>One stage of a block and how long a partition stays in it, in columns of the shared axis.</summary>
public sealed record RotationStage(int StageId, int Periods);

/// <summary>
/// One column of the block's shared axis — the finest window the block distinguishes. Every stage's
/// slots are whole runs of these, which is what makes the dates line up by construction instead of by
/// everyone typing them twice.
/// </summary>
public sealed record RotationColumn(int Number, DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// One <c>StageSlot</c> the apply will write: stage <paramref name="StageId"/>'s period
/// <paramref name="PeriodNumber"/>, spanning columns <paramref name="FirstColumn"/>…<paramref name="LastColumn"/>.
/// </summary>
public sealed record RotationSlot(
    int StageId,
    int PeriodNumber,
    int FirstColumn,
    int LastColumn,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// The whole cycle, worked out but not yet written. <see cref="Matrix"/> is deliberately the type
/// <c>GenerateMacroPlanCommand</c> already consumes: this generates the matrix that used to be ticked by
/// hand, and changes nothing downstream of it.
/// </summary>
public sealed record RotationCycleLayout(
    // Total columns — the sum of the stages' periods, i.e. how long one partition needs to visit every
    // stage of the block.
    int Timeline,
    // The partition count this block requires a multiple of: T / gcd(kₛ). A year holding several blocks
    // needs a P that is a common multiple of all of theirs, since a group carries one partition label for
    // the whole year.
    int PartitionStep,
    IReadOnlyList<RotationColumn> Columns,
    IReadOnlyList<StageTiling> Stages,
    IReadOnlyList<RotationSlot> Slots,
    IReadOnlyList<PartitionStagePlan> Matrix,
    // Legal but probably unintended — never blocking.
    IReadOnlyList<string> Warnings);
