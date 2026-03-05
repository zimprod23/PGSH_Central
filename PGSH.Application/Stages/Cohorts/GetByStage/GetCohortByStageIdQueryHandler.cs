using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Cohorts.GetById;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.GetByStage;

internal sealed class GetCohortByStageIdQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetCohortsByStageQuery, IReadOnlyCollection<CohortResponse>>
{
    public async Task<Result<IReadOnlyCollection<CohortResponse>>> Handle(GetCohortsByStageQuery request, CancellationToken cancellationToken)
    {
        var cohorts = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == request.StageId)
            .OrderByDescending(c => c.Id) // Show newest cohorts first
            .Select(c => new CohortResponse(
                c.Id,
                c.StageId,
                c.Stage.Name,
                c.Label,
                c.RotationTemplates.Count,
                c.Assignments.Count
            ))
            .ToListAsync(cancellationToken);

        return cohorts;
    }
}
