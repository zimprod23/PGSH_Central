using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.GetById;

internal sealed class GetCohortByIdQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetCohortByIdQuery, CohortDetailResponse>
{
    public async Task<Result<CohortDetailResponse>> Handle(
        GetCohortByIdQuery request, CancellationToken cancellationToken)
    {
        var cohort = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.Id == request.Id)
            .Select(c => new CohortDetailResponse(
                c.Id,
                c.StageId,
                c.Stage.Name,
                c.AcademicGroupId,
                c.AcademicGroup.Label,
                c.Label,
                c.Assignments.Count,
                c.RotationTemplates
                    .OrderBy(t => t.SequenceOrder)
                    .Select(t => new RotationTemplateResponse(
                        t.Id,
                        t.ServiceId,
                        t.Service.Name,
                        t.Service.Hospital.Name,
                        t.PlannedStart,
                        t.PlannedEnd,
                        t.SequenceOrder))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        if (cohort is null)
            return Result.Failure<CohortDetailResponse>(Error.NotFound(
                "Cohorts.NotFound",
                $"The cohort with ID {request.Id} was not found."));

        return cohort;
    }
}
