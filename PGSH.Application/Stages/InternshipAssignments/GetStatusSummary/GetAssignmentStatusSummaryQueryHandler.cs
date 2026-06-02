using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.GetStatusSummary;

internal sealed class GetAssignmentStatusSummaryQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetAssignmentStatusSummaryQuery, List<AssignmentStatusCount>>
{
    public async Task<Result<List<AssignmentStatusCount>>> Handle(
        GetAssignmentStatusSummaryQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.InternshipAssignments.AsNoTracking().AsQueryable();

        if (request.CohortIds is { Count: > 0 })
            query = query.Where(a => request.CohortIds.Contains(a.CurrentCohortId));

        if (request.StageId.HasValue)
            query = query.Where(a => a.Cohort.StageId == request.StageId.Value);

        var counts = await query
            .GroupBy(a => a.Status)
            .Select(g => new AssignmentStatusCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return Result.Success(counts);
    }
}
