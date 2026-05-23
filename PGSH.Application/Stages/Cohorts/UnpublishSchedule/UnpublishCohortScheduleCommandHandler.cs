using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.UnpublishSchedule;

internal sealed class UnpublishCohortScheduleCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UnpublishCohortScheduleCommand, int>
{
    public async Task<Result<int>> Handle(UnpublishCohortScheduleCommand request, CancellationToken cancellationToken)
    {
        var assignmentIds = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var periods = await dbContext.ServicePeriods
            .Where(p => assignmentIds.Contains(p.InternshipAssignmentId) && p.CohortSlotAssignmentId != null)
            .ToListAsync(cancellationToken);

        if (periods.Count == 0)
            return Result.Failure<int>(StageErrors.ScheduleNotPublished);

        dbContext.ServicePeriods.RemoveRange(periods);
        await dbContext.SaveChangesAsync(cancellationToken);
        return periods.Count;
    }
}
