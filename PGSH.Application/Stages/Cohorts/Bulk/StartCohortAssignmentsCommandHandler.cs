using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Bulk;

internal sealed class StartCohortAssignmentsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<StartCohortAssignmentsCommand, int>
{
    public async Task<Result<int>> Handle(
        StartCohortAssignmentsCommand request, CancellationToken cancellationToken)
    {
        bool hasServicePeriods = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .SelectMany(a => a.ServicePeriods)
            .AnyAsync(cancellationToken);

        if (!hasServicePeriods)
            return Result.Failure<int>(StageErrors.ScheduleNotPublished);

        var assignments = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.CohortSlotAssignment)
                    .ThenInclude(sa => sa!.StageSlot)
            .Where(a => a.CurrentCohortId == request.CohortId
                     && (a.Status == InternshipStatus.Planned || a.Status == InternshipStatus.Ongoing))
            .ToListAsync(cancellationToken);

        var periodNumbers = request.PeriodNumbers is { Count: > 0 }
            ? request.PeriodNumbers.ToHashSet()
            : null;

        // Starting activates individual periods so chefs only manage what's actually running.
        // Scoped → only periods in those windows; unscoped → every period of the cohort.
        int started = 0;
        foreach (var a in assignments)
        {
            foreach (var period in a.ServicePeriods.Where(p => !p.IsStarted).ToList())
            {
                if (periodNumbers is not null
                    && (period.CohortSlotAssignment is null
                        || !periodNumbers.Contains(period.CohortSlotAssignment.StageSlot.PeriodNumber)))
                    continue;

                var r = a.StartPeriod(period.Id);
                if (r.IsSuccess) started++;
            }
        }

        if (started > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(started);
    }
}
