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
                c.RotationPlanId,
                c.RotationPlan == null
                    ? new List<RotationSlotResponse>()
                    : c.RotationPlan.Slots
                        .OrderBy(s => s.SequenceOrder)
                        .Select(s => new RotationSlotResponse(
                            s.Id,
                            s.ServiceId,
                            s.Service.Name,
                            s.Service.Hospital.Name,
                            s.PlannedStart,
                            s.PlannedEnd,
                            s.SequenceOrder))
                        .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        if (cohort is null)
            return Result.Failure<CohortDetailResponse>(Error.NotFound(
                "Cohorts.NotFound",
                $"The cohort with ID {request.Id} was not found."));

        return cohort;
    }
}
