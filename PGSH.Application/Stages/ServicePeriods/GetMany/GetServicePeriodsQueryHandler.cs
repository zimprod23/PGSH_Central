using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.ServicePeriods.GetMany;

internal sealed class GetServicePeriodsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetServicePeriodsQuery, PaginatedResponse<ServicePeriodResponse>>
{
    public async Task<Result<PaginatedResponse<ServicePeriodResponse>>> Handle(
        GetServicePeriodsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ServicePeriods.AsNoTracking().AsQueryable();

        if (request.AssignmentId.HasValue)
            query = query.Where(p => p.InternshipAssignmentId == request.AssignmentId.Value);

        if (request.ServiceId.HasValue)
            query = query.Where(p => p.ServiceId == request.ServiceId.Value);

        if (request.CohortId.HasValue)
            query = query.Where(p => p.InternshipAssignment.CurrentCohortId == request.CohortId.Value);

        if (request.IsComplete.HasValue)
            query = query.Where(p => p.IsComplete == request.IsComplete.Value);

        var response = await query
            .OrderBy(p => p.StartDate)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                p => new ServicePeriodResponse(
                    p.Id,
                    p.InternshipAssignmentId,
                    (p.InternshipAssignment.Registration.Student.FirstName ?? "") + " " +
                    (p.InternshipAssignment.Registration.Student.LastName ?? ""),
                    p.ServiceId,
                    p.Service.Name,
                    p.Service.Hospital.Name,
                    p.StartDate,
                    p.EndDate,
                    p.IsComplete,
                    p.Evaluation != null),
                cancellationToken);

        return Result.Success(response);
    }
}
