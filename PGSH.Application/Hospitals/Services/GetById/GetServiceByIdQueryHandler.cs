using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.GetById;

internal sealed class GetServiceByIdQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetServiceByIdQuery, ServiceDetailResponse>
{
    public async Task<Result<ServiceDetailResponse>> Handle(GetServiceByIdQuery request, CancellationToken cancellationToken)
    {
        var service = await dbContext.Services
            .AsNoTracking()
            .Include(s => s.Hospital)
            .Include(s => s.ServiceChef)
            .Include(s => s.Staff)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (service is null)
        {
            return Result.Failure<ServiceDetailResponse>(
                Error.NotFound("Services.NotFound", $"Service {request.Id} not found."));
        }

        var response = new ServiceDetailResponse(
            service.Id,
            service.Name,
            service.Description,
            service.ServiceType.ToString(),
            service.Capacity,
            service.HospitalId,
            service.Hospital.Name,
            service.ServiceChef != null ? new ServiceChefResponse(
                service.ServiceChef.Id,
                service.ServiceChef.FirstName,
                service.ServiceChef.LastName,
                service.ServiceChef.PPR,
                service.ServiceChef.Grade.ToString()) : null,
            service.Staff.Select(e => new StaffMemberResponse(
                e.Id,
                e.FirstName,
                e.LastName,
                e.PPR,
                e.Grade.ToString(),
                e.Position?.ToString() ?? "Normal")).ToList()
        );

        return Result.Success(response);
    }
}
