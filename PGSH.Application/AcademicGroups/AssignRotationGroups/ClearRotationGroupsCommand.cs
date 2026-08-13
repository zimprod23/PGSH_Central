using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

/// <summary>
/// Un-partitions a promotion: every group's <c>RotationGroup</c> goes back to null, so the next cut is
/// free to choose any number.
/// </summary>
/// <remarks>
/// <para><b>Why this is needed even though <c>Reassign</c> exists.</b> <c>PartitionAllocator.BuildLabels</c>
/// lets the <em>existing</em> partition count win over the requested one — deliberately, so a gap-fill
/// cannot reshuffle a plan built on the current partitioning. The consequence is that a promotion mistakenly
/// cut into two stays two-way for every later auto-arrange, whatever count is asked for. Clearing is the
/// only way back to "not yet partitioned", and therefore the only way a wrong cut is genuinely undone
/// rather than argued with.</para>
///
/// <para><b>What it does not touch.</b> Nothing points at a partition label: cohorts belong to groups, cells
/// belong to cohorts and slots, periods belong to cells. Clearing the label removes no row and breaks no
/// foreign key — the planning stays exactly as it was and stays executable. What it does mean is that the
/// cells no longer describe any partition, so the crossover they encode has to be rebuilt by arranging
/// again; <see cref="ClearRotationGroupsResult.PlannedCellsAffected"/> is how many.</para>
/// </remarks>
public sealed record ClearRotationGroupsCommand(int AcademicYearId, int? LevelId = null)
    : ICommand<ClearRotationGroupsResult>, IAuditableCommand
{
    public string AuditAction => "PARTITIONS_CLEARED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId.ToString();
    public string? AuditMetadata => $$"""{"levelId":{{LevelId?.ToString() ?? "null"}}}""";
}

/// <param name="PlannedCellsAffected">
/// Cells still planned on the partitioning just removed. They survive untouched, but every one of them was
/// placed for a partition that no longer exists, so an arrange is owed.
/// </param>
public sealed record ClearRotationGroupsResult(
    int Cleared,
    int TotalGroups,
    int PlannedCellsAffected);

internal sealed class ClearRotationGroupsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<ClearRotationGroupsCommand, ClearRotationGroupsResult>
{
    public async Task<Result<ClearRotationGroupsResult>> Handle(
        ClearRotationGroupsCommand request, CancellationToken cancellationToken)
    {
        // Same reach as the assign: a group belongs to a level by LevelId, or — for legacy and
        // auto-arranged groups without one — by having a registration at that level.
        var query = dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId);

        if (request.LevelId.HasValue)
            query = query.Where(g => g.LevelId == request.LevelId
                                  || g.Registrations.Any(r => r.LevelId == request.LevelId));

        var groups = await query.ToListAsync(cancellationToken);

        if (groups.Count == 0)
            return new ClearRotationGroupsResult(0, 0, 0);

        var groupIds = groups.Select(g => g.Id).ToList();

        // Refused for the same reason a re-cut is: a cell backed by a ServicePeriod is an execution
        // record. Students have been sent there, and the printed répartition names the partition they were
        // sent as — removing the label would leave that document unreproducible.
        int publishedCells = await dbContext.ServicePeriods
            .CountAsync(p => p.CohortSlotAssignmentId != null
                          && groupIds.Contains(p.CohortSlotAssignment!.Cohort.AcademicGroupId),
                cancellationToken);

        if (publishedCells > 0)
            return Result.Failure<ClearRotationGroupsResult>(
                PartitionErrors.CannotClearPublished(publishedCells));

        var labelled = groups.Where(g => g.RotationGroup is not null).ToList();

        if (labelled.Count == 0)
            return new ClearRotationGroupsResult(0, groups.Count, 0);

        int plannedCells = await dbContext.CohortSlotAssignments
            .CountAsync(a => groupIds.Contains(a.Cohort.AcademicGroupId), cancellationToken);

        foreach (var group in labelled)
            group.RotationGroup = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ClearRotationGroupsResult(labelled.Count, groups.Count, plannedCells);
    }
}
