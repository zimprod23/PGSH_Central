using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Levels.GetMany;

internal sealed class GetLevelsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetLevelsQuery, PaginatedResponse<LevelResponse>>
{
    public async Task<Result<PaginatedResponse<LevelResponse>>> Handle(
        GetLevelsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Levels.AsNoTracking().AsQueryable();

        if (request.AcademicProgram.HasValue)
            query = query.Where(l => l.AcademicProgram == request.AcademicProgram.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.ToLower();
            query = query.Where(l => l.Label.ToLower().Contains(term));
        }

        var response = await query
            .OrderBy(l => l.Year).ThenBy(l => l.Label)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                l => new LevelResponse(l.Id, l.Label, l.Year, l.AcademicProgram.ToString()),
                cancellationToken);

        return Result.Success(response);
    }
}
