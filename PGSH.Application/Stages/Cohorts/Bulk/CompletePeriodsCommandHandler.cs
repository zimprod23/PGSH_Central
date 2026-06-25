using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Bulk;

internal sealed class CompletePeriodsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CompletePeriodsCommand, int>
{
    public async Task<Result<int>> Handle(
        CompletePeriodsCommand request, CancellationToken cancellationToken)
    {
        var assignments = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.CohortSlotAssignment)
                    .ThenInclude(sa => sa!.StageSlot)
            // Loaded so CompletePeriod can auto-revert a temporary transfer at stage end.
            .Include(a => a.MembershipHistory)
            .Where(a => a.CurrentCohortId == request.CohortId
                     && a.Status == InternshipStatus.Ongoing)
            .ToListAsync(cancellationToken);

        var periodNumbers = request.PeriodNumbers is { Count: > 0 }
            ? request.PeriodNumbers.ToHashSet()
            : null;

        int completed = 0;
        foreach (var a in assignments)
        {
            foreach (var period in a.ServicePeriods.Where(p => !p.IsComplete).ToList())
            {
                // When scoped to specific periods, only close rotations in those windows.
                // Ad-hoc periods (no slot) carry no period number → skipped under a scope.
                if (periodNumbers is not null
                    && (period.CohortSlotAssignment is null
                        || !periodNumbers.Contains(period.CohortSlotAssignment.StageSlot.PeriodNumber)))
                    continue;

                var r = a.CompletePeriod(period.Id);
                if (r.IsSuccess) completed++;
            }
        }

        if (completed > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(completed);
    }
}
