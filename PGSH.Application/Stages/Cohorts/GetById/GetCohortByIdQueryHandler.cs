using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.GetById;

internal sealed class GetCohortByIdQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetCohortByIdQuery, CohortResponse>
{
    public async Task<Result<CohortResponse>> Handle(GetCohortByIdQuery request, CancellationToken cancellationToken)
    {
        var cohort = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.Id == request.Id)
            .Select(c => new CohortResponse(
                c.Id,
                c.StageId,
                c.Stage.Name,
                c.Label,
                c.RotationTemplates.Count,
                c.Assignments.Count
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (cohort is null)
        {
            return Result.Failure<CohortResponse>(Error.NotFound(
                "Cohorts.NotFound",
                $"The cohort with ID {request.Id} was not found."));
        }

        return cohort;
    }
}
