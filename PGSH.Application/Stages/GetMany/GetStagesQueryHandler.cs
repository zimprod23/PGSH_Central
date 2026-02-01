using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.GetMany;

internal class GetStagesQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetStagesQuery, PaginatedResponse<StageSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<StageSummaryResponse>>> Handle(GetStagesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Stages.AsNoTracking();

        if (request.LevelId.HasValue)
        {
            query = query.Where(s => s.LevelId == request.LevelId);
        }
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term));
        }
        // 3. Count before pagination
        int totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(s => s.Name)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new StageSummaryResponse(
                s.Id,
                s.Name,
                s.Coefficient,
                s.DurationInDays,
                s.Level.Label)) // EF Core handles the Join automatically
            .ToListAsync(cancellationToken);

        return Result.Success(new PaginatedResponse<StageSummaryResponse>(
            items,
            request.PageNumber,
            request.PageSize,
            totalCount));
    }
}
