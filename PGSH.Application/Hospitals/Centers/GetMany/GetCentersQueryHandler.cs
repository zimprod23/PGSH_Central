using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Centers.GetMany;

internal sealed class GetCentersQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetCentersQuery, PaginatedResponse<CenterSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<CenterSummaryResponse>>> Handle(GetCentersQuery request, CancellationToken cancellationToken)
    {
        // 1. Setup Query with AsNoTracking
        IQueryable<Center> query = dbContext.Centers.AsNoTracking();

        // 2. Apply Search Filter
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term) ||
                (c.City != null && c.City.ToLower().Contains(term)));
        }
        // 3. Count Total
        int totalCount = await query.CountAsync(cancellationToken);

        // 4. Projection & Pagination
        var items = await query
            .OrderBy(c => c.Name)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new CenterSummaryResponse(
                c.Id,
                c.Name,
                c.CenterType.ToString(),
                c.City,
                c.LocalisationMaps != null ? c.LocalisationMaps.x : null,
                c.LocalisationMaps != null ? c.LocalisationMaps.y : null))
            .ToListAsync(cancellationToken);

        // 5. Wrap in Result and PaginatedResponse
        var response = new PaginatedResponse<CenterSummaryResponse>(
            items,
            request.PageNumber,
            request.PageSize,
            totalCount);

        return Result.Success(response);
    }
}
