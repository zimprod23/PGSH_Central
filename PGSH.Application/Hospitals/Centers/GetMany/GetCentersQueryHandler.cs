using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Centers.GetMany;

internal sealed class GetCentersQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetCentersQuery, PaginatedResponse<CenterSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<CenterSummaryResponse>>> Handle(
        GetCentersQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Centers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) ||
                                     (c.City != null && c.City.ToLower().Contains(term)));
        }

        var response = await query
            .OrderBy(c => c.Name)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                c => new CenterSummaryResponse(
                    c.Id, c.Name, c.CenterType.ToString(), c.City,
                    c.LocalisationMaps != null ? c.LocalisationMaps.x : null,
                    c.LocalisationMaps != null ? c.LocalisationMaps.y : null),
                cancellationToken);

        return Result.Success(response);
    }
}
