using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Extensions;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.ServicePeriods.GetMany;

internal sealed class GetServicePeriodsQueryHandler(
    IApplicationDbContext dbContext,
    IUserContext userContext,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetServicePeriodsQuery, PaginatedResponse<ServicePeriodResponse>>
{
    public async Task<Result<PaginatedResponse<ServicePeriodResponse>>> Handle(
        GetServicePeriodsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ServicePeriods.AsNoTracking().AsQueryable();

        // Global administrative staff read across every service. A service-scoped user
        // (chef or secretary staffing a service) reads only his own services' periods —
        // this is the shared read behind the presence grid. Anyone else is refused.
        if (!Roles.Administrative.Any(userContext.IsInRole))
        {
            var myServices = await authorizer.MyServiceIdsAsync(cancellationToken);
            if (myServices.Count == 0)
                return Result.Failure<PaginatedResponse<ServicePeriodResponse>>(StageErrors.AdministrativeOnly);

            query = query.Where(p => myServices.Contains(p.ServiceId));
        }

        if (request.AssignmentId.HasValue)
            query = query.Where(p => p.InternshipAssignmentId == request.AssignmentId.Value);

        if (request.ServiceId.HasValue)
            query = query.Where(p => p.ServiceId == request.ServiceId.Value);

        if (request.CohortId.HasValue)
            query = query.Where(p => p.InternshipAssignment.CurrentCohortId == request.CohortId.Value);

        if (request.IsComplete.HasValue)
            query = query.Where(p => p.IsComplete == request.IsComplete.Value);

        if (request.AcademicYearId.HasValue)
            query = query.Where(p => p.InternshipAssignment.Registration.AcademicYearId == request.AcademicYearId.Value);

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
                    p.InternshipAssignment.Registration.Student.CNE,
                    p.InternshipAssignment.Registration.Student.Appogee,
                    p.ServiceId,
                    p.Service.Name,
                    p.Service.Hospital.Name,
                    p.StartDate,
                    p.EndDate,
                    p.IsComplete,
                    p.Evaluation != null,
                    p.InternshipAssignment.Cohort.AcademicGroup.Label,
                    p.InternshipAssignment.Cohort.Stage.Name,
                    p.InternshipAssignment.Cohort.Stage.Level.Label,
                    null,
                    p.IsPaused,
                    p.Pauses.Where(x => x.ResumeDate == null).Select(x => x.Reason).FirstOrDefault(),
                    p.IsInterrupted),
                cancellationToken);

        return Result.Success(response);
    }
}
