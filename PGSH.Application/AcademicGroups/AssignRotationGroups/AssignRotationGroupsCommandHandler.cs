using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Repartition;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

internal sealed class AssignRotationGroupsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AssignRotationGroupsCommand, PartitionAssignmentResult>
{
    public async Task<Result<PartitionAssignmentResult>> Handle(
        AssignRotationGroupsCommand request, CancellationToken cancellationToken)
    {
        if (request.PartitionCount < 1)
            return Result.Failure<PartitionAssignmentResult>(
                Error.Validation("Partitions.InvalidCount", "Partition count must be at least 1."));

        // A partition divides a promotion. « Retrait » (year 0) is a withdrawal marker the legacy
        // import kept as a level — see Level.IsPromotion — so cutting it would describe a division of
        // the withdrawn, and it is exactly how one of its rosters came to carry a label.
        // ⚠ The clear is deliberately NOT guarded: it is how such a label is taken back off.
        var level = await dbContext.Levels
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LevelId, cancellationToken);

        if (level is null)
            return Result.Failure<PartitionAssignmentResult>(LevelErrors.NotFound(request.LevelId));

        if (!level.IsPromotion)
            return Result.Failure<PartitionAssignmentResult>(
                LevelErrors.NotAPromotion(level.Label ?? $"niveau {request.LevelId}"));

        // Partitions are scoped per (year, level): different levels have different partition counts,
        // and one count applied across them is not a cut of anything.
        //
        // ⚠ The promotion is read from LevelId alone. Falling back to "has a registration at that
        // level" was how legacy rosters — which carried no LevelId — were reached, and it is exactly
        // wrong here: it also reaches « Non réparti », which holds every promotion's unassigned
        // students at once, so cutting one level handed a partition label to a bucket of 4,725 people.
        // SplitAcademicGroupsPerLevel gave every real roster its promotion; the only rows still
        // without one are the buckets, and a bucket is not a rotation partition. Equality against a
        // non-null LevelId excludes them by construction — which is why the parameter is required.
        var groups = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId && g.LevelId == request.LevelId)
            .OrderBy(g => g.GroupNumber)
            .ToListAsync(cancellationToken);

        if (groups.Count == 0)
            return new PartitionAssignmentResult(0, 0, 0, 0, []);

        var groupIds = groups.Select(g => g.Id).ToList();

        Dictionary<int, string> assignments;
        int reassigned = 0;

        if (request.Reassign)
        {
            // A cell backed by a ServicePeriod is an execution record: students have been sent there,
            // and possibly already served. Re-cutting the partitions under it would leave the published
            // plan describing a partitioning that no longer exists, so it is refused outright rather
            // than reported — unlike the merely-planned cells below, which an arrange can rebuild.
            int publishedCells = await dbContext.ServicePeriods
                .CountAsync(p => p.CohortSlotAssignmentId != null
                              && groupIds.Contains(p.CohortSlotAssignment!.Cohort.AcademicGroupId),
                    cancellationToken);

            if (publishedCells > 0)
                return Result.Failure<PartitionAssignmentResult>(
                    PartitionErrors.CannotReassignPublished(publishedCells));

            assignments = PartitionAllocator.ReassignAll(
                groupIds, request.PartitionCount, request.Strategy);

            reassigned = groups.Count(g => g.RotationGroup is not null
                                        && assignments.GetValueOrDefault(g.Id) != g.RotationGroup);
        }
        else
        {
            assignments = PartitionAllocator.AssignUnlabelled(
                groups.Select(g => (g.Id, g.RotationGroup)).ToList(),
                request.PartitionCount,
                request.Strategy);
        }

        int labeled = groups.Count(g => g.RotationGroup is null && assignments.ContainsKey(g.Id));

        foreach (var group in groups.Where(g => assignments.ContainsKey(g.Id)))
            group.RotationGroup = assignments[group.Id];

        // Counted after the new labels are known, because what matters is how many planned cells sit on
        // a group whose partition actually moved — not how many exist.
        int plannedCellsAffected = reassigned == 0
            ? 0
            : await dbContext.CohortSlotAssignments
                .CountAsync(a => groupIds.Contains(a.Cohort.AcademicGroupId), cancellationToken);

        if (assignments.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return new PartitionAssignmentResult(
            labeled,
            reassigned,
            groups.Count,
            plannedCellsAffected,
            Membership(groups));
    }

    /// <summary>Each partition's membership, printed the way the répartition prints a cell.</summary>
    private static List<PartitionMembership> Membership(List<AcademicGroup> groups) =>
        groups
            .Where(g => g.RotationGroup is not null)
            .GroupBy(g => g.RotationGroup!)
            .OrderBy(g => g.Key)
            .Select(g => new PartitionMembership(
                g.Key,
                g.Count(),
                GroupNumberRanges.Format(g.Select(x => x.GroupNumber))))
            .ToList();
}
