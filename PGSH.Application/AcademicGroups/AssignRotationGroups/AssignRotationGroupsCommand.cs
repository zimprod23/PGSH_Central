using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

/// <summary>
/// Divides a promotion's groups between rotation partitions — the act the whole macro plan rests on,
/// since a partition is what gets sent into one stage's window while another partition takes the other.
/// </summary>
/// <param name="Strategy">
/// How to cut. <see cref="PartitionStrategy.Interleaved"/> (the default, and what PGSH has always done)
/// stripes the promotion; <see cref="PartitionStrategy.Contiguous"/> cuts it into blocks the way the
/// faculty's published table does.
/// </param>
/// <param name="Reassign">
/// Re-cut groups that already carry a partition, instead of only filling the ones that do not. Off by
/// default: moving a group between partitions is precisely what an existing plan encodes.
/// </param>
/// <param name="LevelId">
/// The promotion being cut. ⚠ <b>Required, and it is the guard rather than a filter.</b> Left optional
/// this reached every promotion of the year at once — folding three numberings whose partition counts
/// differ (2 in 1Med, 10 in 6Med) into one call, where <c>PartitionAllocator.BuildLabels</c> then
/// resolves a single count for all of them — and it reached « Non réparti », the one roster that
/// belongs to no promotion and must never carry a partition, since labelling it moves every promotion's
/// unassigned registrations as one body (4,725 of them in 2025-2026). A cut has no meaning outside a
/// promotion, so the type says so.
/// </param>
public sealed record AssignRotationGroupsCommand(
    int AcademicYearId,
    int PartitionCount,
    int LevelId,
    PartitionStrategy Strategy = PartitionStrategy.Interleaved,
    bool Reassign = false) : ICommand<PartitionAssignmentResult>;

/// <summary>
/// What the cut produced. <paramref name="Partitions"/> prints each partition's membership in the same
/// collapsed form the répartition uses, which is what makes the two strategies comparable at a glance —
/// <c>A: 1, 3, 5, 7…</c> against <c>A: 1-40</c> — without having to arrange anything first.
/// </summary>
public sealed record PartitionAssignmentResult(
    int Labeled,
    int Reassigned,
    int TotalGroups,
    // Cells already planned on the previous partitioning. They are not touched, but every one of them
    // was placed for a partition the group may no longer be in, so an arrange has to be re-run.
    int PlannedCellsAffected,
    IReadOnlyList<PartitionMembership> Partitions);

public sealed record PartitionMembership(string Label, int GroupCount, string GroupNumbers);
