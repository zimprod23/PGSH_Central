using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.GetMany;

internal sealed class GetManyHospitalsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetHospitalsQuery, PaginatedResponse<HospitalSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<HospitalSummaryResponse>>> Handle(
        GetHospitalsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Hospitals.AsNoTracking().Include(h => h.Center).AsQueryable();

        if (request.CenterId.HasValue)
            query = query.Where(h => h.CenterId == request.CenterId.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(h => h.Name.ToLower().Contains(term) || h.City.ToLower().Contains(term));
        }

        var response = await query
            .OrderBy(h => h.Name)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                h => new HospitalSummaryResponse(
                    h.Id, h.Name, h.CenterId, h.Center.Name, h.HospitalType.ToString(), h.City, h.Email,
                    h.Description,
                    h.LocalisationMaps != null ? h.LocalisationMaps.x : null,
                    h.LocalisationMaps != null ? h.LocalisationMaps.y : null,
                    h.LocalisationMaps != null ? h.LocalisationMaps.z : null),
                cancellationToken);

        return Result.Success(response);
    }
}
