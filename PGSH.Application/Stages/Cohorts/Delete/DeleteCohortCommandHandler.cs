using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Delete;

internal sealed class DeleteCohortCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<DeleteCohortCommand>
{
    public async Task<Result> Handle(DeleteCohortCommand request, CancellationToken cancellationToken)
    {
        var cohort = await dbContext.Cohorts
            .FirstOrDefaultAsync(c => c.Id == request.CohortId, cancellationToken);

        if (cohort is null)
            return Result.Failure(Error.NotFound(
                "Cohorts.NotFound",
                $"The cohort with Id = '{request.CohortId}' was not found."));

        // Block deletion if any assignment is beyond Planned (in-progress or evaluated)
        bool hasActiveAssignments = await dbContext.InternshipAssignments
            .AnyAsync(a => a.CurrentCohortId == request.CohortId
                        && a.Status != InternshipStatus.Planned,
                      cancellationToken);

        if (hasActiveAssignments)
            return Result.Failure(Error.Conflict(
                "Cohorts.HasActiveAssignments",
                "This cohort has active or completed assignments and cannot be deleted. Remove or transfer students first."));

        var assignmentIds = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        // 1. Delete plan-generated and ad-hoc service periods for these assignments
        var periods = await dbContext.ServicePeriods
            .Where(p => assignmentIds.Contains(p.InternshipAssignmentId))
            .ToListAsync(cancellationToken);
        dbContext.ServicePeriods.RemoveRange(periods);

        // 2. Delete all assignments (MembershipHistory cascades via EF)
        var assignments = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .ToListAsync(cancellationToken);
        dbContext.InternshipAssignments.RemoveRange(assignments);

        // 3. Delete CohortMembership records that reference this cohort from transfers
        //    (these belong to assignments in other cohorts — not covered by the cascade above)
        var orphanMemberships = await dbContext.CohortMembership
            .Where(m => m.CohortId == request.CohortId)
            .ToListAsync(cancellationToken);
        dbContext.CohortMembership.RemoveRange(orphanMemberships);

        dbContext.Cohorts.Remove(cohort);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
