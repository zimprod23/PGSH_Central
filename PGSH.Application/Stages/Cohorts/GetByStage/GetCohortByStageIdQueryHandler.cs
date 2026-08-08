using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Application.Stages.Cohorts.GetById;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.GetByStage;

internal sealed class GetCohortByStageIdQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetCohortsByStageQuery, PaginatedResponse<CohortResponse>>
{
    public async Task<Result<PaginatedResponse<CohortResponse>>> Handle(
        GetCohortsByStageQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == request.StageId);

        if (request.AcademicYearId.HasValue)
            query = query.Where(c => c.AcademicGroup.AcademicYearId == request.AcademicYearId.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(c =>
                c.Label.ToLower().Contains(term)
             || c.AcademicGroup.Label.ToLower().Contains(term));
        }

        var cohorts = await query
            .OrderBy(c => c.AcademicGroup.GroupNumber)
            .ThenBy(c => c.Id)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                c => new CohortResponse(
                    c.Id,
                    c.StageId,
                    c.Stage.Name,
                    c.AcademicGroupId,
                    c.AcademicGroup.Label,
                    c.Label,
                    c.Assignments.Count,
                    c.SlotAssignments.Count,
                    c.Assignments.Any(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null)),
                    c.AcademicGroup.AcademicYearId,
                    c.AcademicGroup.AcademicYear.Label,
                    c.AcademicGroup.RotationGroup),
                cancellationToken);

        return cohorts;
    }
}
