using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.GetById;

internal sealed class GetHospitalByIdQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetHospitalByIdQuery, HospitalDetailResponse>
{
    public async Task<Result<HospitalDetailResponse>> Handle(GetHospitalByIdQuery request, CancellationToken cancellationToken)
    {
        var hospital = await dbContext.Hospitals
                                .AsNoTracking()
                                .Include(h => h.Center)
                                .Include(h => h.services)
                                .FirstOrDefaultAsync(h => h.Id == request.Id, cancellationToken);
        if(hospital is null)
        {
            return Result.Failure<HospitalDetailResponse>(
                Error.NotFound("Hospitals.NotFound", $"Hospital with ID {request.Id} not found."));
        }

        // 3. Map to Detail Response
        var response = new HospitalDetailResponse(
            hospital.Id,
            hospital.Name,
            hospital.CenterId,
            hospital.Center.Name,
            hospital.HospitalType.ToString(),
            hospital.City,
            hospital.Description,
            hospital.Email,
            hospital.LocalisationMaps?.x,
            hospital.LocalisationMaps?.y,
            hospital.services.Select(s => new ServiceSummaryResponse(
                s.Id,
                s.Name,
                s.Capacity,
                s.ServiceChef != null ? $"{s.ServiceChef.FirstName} {s.ServiceChef.LastName}" : "Not Assigned"
            )).ToList()
        );

        return Result.Success(response);

    }
}
