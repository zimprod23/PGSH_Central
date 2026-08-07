using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.GetMany;

internal sealed class GetStagesQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetStagesQuery, PaginatedResponse<StageSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<StageSummaryResponse>>> Handle(
        GetStagesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Stages.AsNoTracking().AsQueryable();

        if (request.LevelId.HasValue)
            query = query.Where(s => s.LevelId == request.LevelId);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term));
        }

        var response = await query
            .OrderBy(s => s.Name)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                s => new StageSummaryResponse(s.Id, s.Name, s.Coefficient, s.DurationInDays, s.Level.Label),
                cancellationToken);

        return Result.Success(response);
    }
}
