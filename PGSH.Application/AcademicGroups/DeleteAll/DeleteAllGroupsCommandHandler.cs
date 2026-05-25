using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.DeleteAll;

internal sealed class DeleteAllGroupsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<DeleteAllGroupsCommand, int>
{
    public async Task<Result<int>> Handle(DeleteAllGroupsCommand request, CancellationToken cancellationToken)
    {
        var groupIds = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        if (groupIds.Count == 0)
            return Result.Success(0);

        bool hasStudents = await dbContext.Registrations
            .AnyAsync(r => r.AcademicGroupId != null && groupIds.Contains(r.AcademicGroupId.Value), cancellationToken);

        if (hasStudents)
            return Result.Failure<int>(Error.Conflict(
                "AcademicGroups.HasStudents",
                "One or more groups in this year have students assigned. Empty all groups before deleting them."));

        var cohortIds = await dbContext.Cohorts
            .Where(c => groupIds.Contains(c.AcademicGroupId))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (cohortIds.Count > 0)
        {
            bool hasActiveAssignments = await dbContext.InternshipAssignments
                .AnyAsync(a => cohortIds.Contains(a.CurrentCohortId)
                            && a.Status != InternshipStatus.Planned,
                          cancellationToken);

            if (hasActiveAssignments)
                return Result.Failure<int>(Error.Conflict(
                    "Cohorts.HasActiveAssignments",
                    "One or more cohorts linked to these groups have assignments that have already started. Reset cohorts first."));

            var assignmentIds = await dbContext.InternshipAssignments
                .Where(a => cohortIds.Contains(a.CurrentCohortId))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            if (assignmentIds.Count > 0)
            {
                await dbContext.ServicePeriods
                    .Where(p => assignmentIds.Contains(p.InternshipAssignmentId))
                    .ExecuteDeleteAsync(cancellationToken);

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

            await dbContext.Cohorts
                .Where(c => groupIds.Contains(c.AcademicGroupId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        int deleted = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .ExecuteDeleteAsync(cancellationToken);

        return Result.Success(deleted);
    }
}
