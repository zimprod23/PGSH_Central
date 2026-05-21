using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.GetMany;

internal class GetServicesQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetServicesQuery, PaginatedResponse<ServiceSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<ServiceSummaryResponse>>> Handle(GetServicesQuery request, CancellationToken cancellationToken)
    {
        IQueryable<Service> query = dbContext.Services.AsNoTracking();

        if (request.HospitalId.HasValue)
            query = query.Where(s => s.HospitalId == request.HospitalId.Value);

        if (request.ServiceType.HasValue)
            query = query.Where(s => s.ServiceType == request.ServiceType.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term));
        }

        var response = await query
            .OrderBy(s => s.Name)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                s => new ServiceSummaryResponse(
                    s.Id, s.Name, s.ServiceType.ToString(), s.Specialty, s.Capacity,
                    s.HospitalId, s.Hospital.Name,
                    s.ServiceChef != null ? s.ServiceChef.FirstName + " " + s.ServiceChef.LastName : null,
                    // Staff is a field not a property; EF cannot translate field navigation in LINQ-to-SQL
                    0),
                cancellationToken);

        return Result.Success(response);
    }
}
