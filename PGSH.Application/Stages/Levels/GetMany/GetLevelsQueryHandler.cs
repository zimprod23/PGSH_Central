using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Students.GetById;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Levels.GetMany;

internal sealed class GetLevelsQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetLevelsQuery, PaginatedResponse<LevelResponse>>
{
    public async Task<Result<PaginatedResponse<LevelResponse>>> Handle(GetLevelsQuery request, CancellationToken cancellationToken)
    {
        // 1. Base Query with NoTracking
        var query = dbContext.Levels.AsNoTracking();

        // 2. Filters
        if (request.AcademicProgram.HasValue)
        {
            query = query.Where(l => (int)l.AcademicProgram == request.AcademicProgram);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.ToLower();
            query = query.Where(l => l.Label.ToLower().Contains(term));
        }

        // 3. Count
        int totalCount = await query.CountAsync(cancellationToken);

        // 4. Pagination & Projection
        var items = await query
            .OrderBy(l => l.Year)
            .ThenBy(l => l.Label)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(l => new LevelResponse(
                //l.Id,
                l.Label,
                l.Year,
                l.AcademicProgram.ToString()))
            .ToListAsync(cancellationToken);

        return Result.Success(new PaginatedResponse<LevelResponse>(
            items,
            request.PageNumber,
            request.PageSize,
            totalCount));
    }
}
