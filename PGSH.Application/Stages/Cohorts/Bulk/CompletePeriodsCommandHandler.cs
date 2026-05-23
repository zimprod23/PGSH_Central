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
            .Where(a => a.CurrentCohortId == request.CohortId
                     && a.Status == InternshipStatus.Ongoing)
            .ToListAsync(cancellationToken);

        int completed = 0;
        foreach (var a in assignments)
        {
            foreach (var period in a.ServicePeriods.Where(p => !p.IsComplete).ToList())
            {
                var r = a.CompletePeriod(period.Id);
                if (r.IsSuccess) completed++;
            }
        }

        if (completed > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(completed);
    }
}
