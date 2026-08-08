using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.GetMany;

internal sealed class GetAcademicGroupsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetAcademicGroupsQuery, PaginatedResponse<AcademicGroupResponse>>
{
    public async Task<Result<PaginatedResponse<AcademicGroupResponse>>> Handle(
        GetAcademicGroupsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.AcademicGroups.AsNoTracking().AsQueryable();

        if (request.AcademicYearId.HasValue)
            query = query.Where(g => g.AcademicYearId == request.AcademicYearId.Value);

        if (request.LevelId.HasValue)
            query = query.Where(g => g.LevelId == request.LevelId.Value
                                  || g.Registrations.Any(r => r.LevelId == request.LevelId.Value));

        if (request.StudentId.HasValue)
            query = query.Where(g => g.Registrations.Any(r => r.StudentId == request.StudentId.Value));

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(g => g.Label.ToLower().Contains(term));
        }

        var groups = await query
            .OrderBy(g => g.AcademicYearId)
            .ThenBy(g => g.GroupNumber)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                g => new AcademicGroupResponse(
                    g.Id,
                    g.Label,
                    g.GroupNumber,
                    g.AcademicYearId,
                    g.AcademicYear.Label,
                    g.RotationGroup,
                    g.LevelId,
                    g.Level != null ? g.Level.Label : null,
                    g.Registrations.Count),
                cancellationToken);

        return groups;
    }
}
