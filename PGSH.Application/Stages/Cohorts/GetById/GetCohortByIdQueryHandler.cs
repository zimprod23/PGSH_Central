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
                c.Assignments.Any(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null)),
                c.SlotAssignments
                    .OrderBy(a => a.StageSlot.PeriodNumber)
                    .Select(a => new CohortSlotDetail(
                        a.Id,
                        a.StageSlotId,
                        a.StageSlot.PeriodNumber,
                        a.StageSlot.Label,
                        a.StageSlot.StartDate,
                        a.StageSlot.EndDate,
                        a.ServiceId,
                        a.Service.Name,
                        a.Service.Hospital.Name))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        if (cohort is null)
            return Result.Failure<CohortDetailResponse>(Error.NotFound(
                "Cohorts.NotFound",
                $"The cohort with ID {request.Id} was not found."));

        return cohort;
    }
}
