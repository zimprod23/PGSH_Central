using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.GetMany;

internal sealed class GetAcademicGroupsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetAcademicGroupsQuery, List<AcademicGroupResponse>>
{
    public async Task<Result<List<AcademicGroupResponse>>> Handle(
        GetAcademicGroupsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.AcademicGroups.AsNoTracking().AsQueryable();

        if (request.AcademicYearId.HasValue)
            query = query.Where(g => g.AcademicYearId == request.AcademicYearId.Value);

        if (request.LevelId.HasValue)
            query = query.Where(g => g.Registrations.Any(r => r.LevelId == request.LevelId.Value));

        var groups = await query
            .OrderBy(g => g.AcademicYearId)
            .ThenBy(g => g.GroupNumber)
            .Select(g => new AcademicGroupResponse(
                g.Id,
                g.Label,
                g.GroupNumber,
                g.AcademicYearId,
                g.AcademicYear.Label,
                g.RotationGroup))
            .ToListAsync(cancellationToken);

        return Result.Success(groups);
    }
}
