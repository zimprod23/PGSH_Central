using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
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
            .Include(s => s.LevelCapacities)
                .ThenInclude(c => c.Level)
            .Include(s => s.ChefHistory)
                .ThenInclude(h => h.Employee)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (service is null)
        {
            return Result.Failure<ServiceDetailResponse>(ServiceErrors.NotFound(request.Id));
        }

        var localization = service.LocalisationMaps ?? service.Hospital.LocalisationMaps;

        var response = new ServiceDetailResponse(
            service.Id,
            service.Name,
            service.Description,
            service.ServiceType.ToString(),
            service.Specialty,
            service.Capacity,
            service.HospitalId,
            service.Hospital.Name,
            service.Hospital.City,
            service.Hospital.Description,
            localization?.x,
            localization?.y,
            localization?.z,
            service.LocalisationMaps is not null,
            service.ServiceChef != null ? new ServiceChefResponse(
                service.ServiceChef.Id,
                service.ServiceChef.FirstName,
                service.ServiceChef.LastName,
                service.ServiceChef.PPR,
                service.ServiceChef.Grade.ToString()) : null,
            service.LevelCapacities
                .OrderBy(c => c.Level.AcademicProgram)
                .ThenBy(c => c.Level.Year)
                .Select(c => new ServiceLevelCapacityResponse(
                    c.LevelId,
                    c.Level.Label,
                    c.Level.Year,
                    c.Level.AcademicProgram.ToString(),
                    c.Capacity))
                .ToList(),
            service.Staff.Select(e => new StaffMemberResponse(
                e.Id,
                e.FirstName,
                e.LastName,
                e.PPR,
                e.Grade.ToString(),
                e.Position?.ToString() ?? "Normal")).ToList(),
            service.ChefHistory
                .OrderByDescending(h => h.StartDate)
                .Select(h => new ChefTenureResponse(
                    h.EmployeeId,
                    h.Employee.FirstName,
                    h.Employee.LastName,
                    h.Employee.Grade.ToString(),
                    h.StartDate,
                    h.EndDate))
                .ToList(),
            // Parsed here rather than in SQL: it is free text with a known prefix, and the format
            // lives in one place so the importer that writes it and every reader cannot drift.
            ServiceChefSourceNote.Read(service.Description)
        );

        return Result.Success(response);
    }
}
