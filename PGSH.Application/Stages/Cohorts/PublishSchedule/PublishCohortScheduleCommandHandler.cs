using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

internal sealed class PublishCohortScheduleCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<PublishCohortScheduleCommand>
{
    public async Task<Result> Handle(PublishCohortScheduleCommand request, CancellationToken cancellationToken)
    {
        var cohort = await dbContext.Cohorts
            .FirstOrDefaultAsync(c => c.Id == request.CohortId, cancellationToken);

        if (cohort is null)
            return Result.Failure(StageErrors.CohortNotFound(request.CohortId));

        bool alreadyPublished = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .AnyAsync(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null), cancellationToken);

        if (alreadyPublished)
            return Result.Failure(StageErrors.ScheduleAlreadyPublished);

        var slotAssignments = await dbContext.CohortSlotAssignments
            .Where(a => a.CohortId == request.CohortId)
            .Select(a => new
            {
                a.Id,
                a.StageSlotId,
                a.ServiceId,
                ServiceName     = a.Service.Name,
                ServiceCapacity = a.Service.Capacity,
                SlotPeriod      = a.StageSlot.PeriodNumber,
                SlotStart       = a.StageSlot.StartDate,
                SlotEnd         = a.StageSlot.EndDate,
            })
            .ToListAsync(cancellationToken);

        if (slotAssignments.Count == 0)
            return Result.Failure(StageErrors.ScheduleNotConfigured);

        var assignments = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
            return Result.Failure(StageErrors.NoPlannedAssignments);

        // Generate ServicePeriods
        var newPeriods = new List<ServicePeriod>();
        foreach (var sa in slotAssignments)
        {
            foreach (var a in assignments)
            {
                newPeriods.Add(new ServicePeriod
                {
                    InternshipAssignmentId = a.Id,
                    ServiceId              = sa.ServiceId,
                    CohortSlotAssignmentId = sa.Id,
                    StartDate              = sa.SlotStart,
                    EndDate                = sa.SlotEnd,
                    IsComplete             = false,
                });
            }
        }

        await dbContext.ServicePeriods.AddRangeAsync(newPeriods, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
