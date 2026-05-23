using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.PublishRotation;

internal sealed class PublishRotationCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<PublishRotationCommand, int>
{
    public async Task<Result<int>> Handle(
        PublishRotationCommand request, CancellationToken cancellationToken)
    {
        var cohort = await dbContext.Cohorts
            .Include(c => c.RotationPlan)
                .ThenInclude(p => p!.Slots)
            .FirstOrDefaultAsync(c => c.Id == request.CohortId, cancellationToken);

        if (cohort is null)
            return Result.Failure<int>(StageErrors.CohortNotFound(request.CohortId));

        if (cohort.RotationPlan is null)
            return Result.Failure<int>(StageErrors.RotationPlanNotAssigned);

        var assignmentIds = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        bool alreadyPublished = await dbContext.ServicePeriods
            .AnyAsync(p => p.RotationPlanSlotId != null
                        && assignmentIds.Contains(p.InternshipAssignmentId),
                      cancellationToken);

        if (alreadyPublished)
            return Result.Failure<int>(StageErrors.RotationAlreadyPublished);

        var assignments = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId
                     && a.Status == InternshipStatus.Planned)
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
            return Result.Failure<int>(StageErrors.NoPlannedAssignments);

        var slots = cohort.RotationPlan.Slots
            .OrderBy(s => s.SequenceOrder)
            .ToList();

        var periods = new List<ServicePeriod>(assignments.Count * slots.Count);
        foreach (var assignment in assignments)
        {
            foreach (var slot in slots)
            {
                periods.Add(new ServicePeriod
                {
                    Id                     = Guid.NewGuid(),
                    InternshipAssignmentId = assignment.Id,
                    ServiceId              = slot.ServiceId,
                    RotationPlanSlotId     = slot.Id,
                    StartDate              = slot.PlannedStart,
                    EndDate                = slot.PlannedEnd,
                    IsComplete             = false,
                });
            }
        }

        dbContext.ServicePeriods.AddRange(periods);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(assignments.Count);
    }
}
