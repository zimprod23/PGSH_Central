using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Bulk;

internal sealed class StartCohortAssignmentsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<StartCohortAssignmentsCommand, int>
{
    public async Task<Result<int>> Handle(
        StartCohortAssignmentsCommand request, CancellationToken cancellationToken)
    {
        var assignments = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId
                     && a.Status == InternshipStatus.Planned)
            .ToListAsync(cancellationToken);

        int started = 0;
        foreach (var a in assignments)
        {
            var r = a.Start();
            if (r.IsSuccess) started++;
        }

        if (started > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(started);
    }
}
