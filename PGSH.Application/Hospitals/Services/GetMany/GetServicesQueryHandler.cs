using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.GetMany;

internal class GetServicesQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetServicesQuery, PaginatedResponse<ServiceSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<ServiceSummaryResponse>>> Handle(GetServicesQuery request, CancellationToken cancellationToken)
    {
        // 1. Setup Query
        IQueryable<Service> query = dbContext.Services.AsNoTracking();

        // 2. Apply Filters
        if (request.HospitalId.HasValue)
            query = query.Where(s => s.HospitalId == request.HospitalId.Value);

        if (request.ServiceType.HasValue)
            query = query.Where(s => (int)s.ServiceType == request.ServiceType.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term));
        }
        // 3. Total Count
        int totalCount = await query.CountAsync(cancellationToken);

        // 4. Projection
        // We project to include the parent Hospital Name and the Chef's full name
        var items = await query
            .OrderBy(s => s.Name)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new ServiceSummaryResponse(
                s.Id,
                s.Name,
                s.ServiceType.ToString(),
                s.Capacity,
                s.HospitalId,
                s.Hospital.Name,
                s.ServiceChef != null
                    ? s.ServiceChef.FirstName + " " + s.ServiceChef.LastName
                    : null,
                s.Staff.Count))
            .ToListAsync(cancellationToken);

        var response = new PaginatedResponse<ServiceSummaryResponse>(
            items,
            request.PageNumber,
            request.PageSize,
            totalCount);

        return Result.Success(response);
    }
}
