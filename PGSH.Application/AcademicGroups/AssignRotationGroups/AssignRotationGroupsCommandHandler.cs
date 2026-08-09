using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Repartition;
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

        // Partitions are scoped per (year, level): different levels can have different
        // partition counts. A group belongs to a level by its LevelId, or — for legacy/
        // auto-arranged groups without one — by having a registration at that level.
        var query = dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId);

        if (request.LevelId.HasValue)
            query = query.Where(g => g.LevelId == request.LevelId
                                  || g.Registrations.Any(r => r.LevelId == request.LevelId));

        var groups = await query
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
