using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.GetMany;

internal sealed class GetInternshipAssignmentsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetInternshipAssignmentsQuery, PaginatedResponse<InternshipAssignmentSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<InternshipAssignmentSummaryResponse>>> Handle(
        GetInternshipAssignmentsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.InternshipAssignments.AsNoTracking().AsQueryable();

        if (request.CohortIds is { Count: > 0 })
            query = query.Where(a => request.CohortIds.Contains(a.CurrentCohortId));

        if (request.RegistrationId.HasValue)
            query = query.Where(a => a.RegistrationId == request.RegistrationId.Value);

        if (request.Status.HasValue)
            query = query.Where(a => a.Status == request.Status.Value);

        if (request.StageId.HasValue)
            query = query.Where(a => a.Cohort.StageId == request.StageId.Value);

        var response = await query
            .OrderBy(a => a.Registration.Student.LastName)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                a => new InternshipAssignmentSummaryResponse(
                    a.Id,
                    a.RegistrationId,
                    (a.Registration.Student.FirstName ?? "") + " " + (a.Registration.Student.LastName ?? ""),
                    a.CurrentCohortId,
                    a.Cohort.Label,
                    a.Cohort.StageId,
                    a.Cohort.Stage.Name,
                    a.Status,
                    a.FinalScore,
                    a.Result),
                cancellationToken);

        return Result.Success(response);
    }
}
