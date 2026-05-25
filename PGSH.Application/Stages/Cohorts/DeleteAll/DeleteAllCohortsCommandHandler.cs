using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.DeleteAll;

internal sealed class DeleteAllCohortsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<DeleteAllCohortsCommand, int>
{
    public async Task<Result<int>> Handle(DeleteAllCohortsCommand request, CancellationToken cancellationToken)
    {
        var cohortIds = await dbContext.Cohorts
            .Where(c => c.StageId == request.StageId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (cohortIds.Count == 0)
            return Result.Success(0);

        bool hasActiveAssignments = await dbContext.InternshipAssignments
            .AnyAsync(a => cohortIds.Contains(a.CurrentCohortId)
                        && a.Status != InternshipStatus.Planned,
                      cancellationToken);

        if (hasActiveAssignments)
            return Result.Failure<int>(Error.Conflict(
                "Cohorts.HasActiveAssignments",
                "One or more cohorts in this stage have assignments that have already started. Students must be completed or transferred before resetting."));

        var assignmentIds = await dbContext.InternshipAssignments
            .Where(a => cohortIds.Contains(a.CurrentCohortId))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (assignmentIds.Count > 0)
        {
            await dbContext.ServicePeriods
                .Where(p => assignmentIds.Contains(p.InternshipAssignmentId))
                .ExecuteDeleteAsync(cancellationToken);

            // Delete membership records linked to these assignments (cascade path)
            // and records that point to these cohorts from transfers (orphan path)
            await dbContext.CohortMembership
                .Where(m => assignmentIds.Contains(m.InternshipAssignmentId)
                         || cohortIds.Contains(m.CohortId))
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.InternshipAssignments
                .Where(a => cohortIds.Contains(a.CurrentCohortId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await dbContext.CohortSlotAssignments
            .Where(a => cohortIds.Contains(a.CohortId))
            .ExecuteDeleteAsync(cancellationToken);

        int deleted = await dbContext.Cohorts
            .Where(c => c.StageId == request.StageId)
            .ExecuteDeleteAsync(cancellationToken);

        return Result.Success(deleted);
    }
}
