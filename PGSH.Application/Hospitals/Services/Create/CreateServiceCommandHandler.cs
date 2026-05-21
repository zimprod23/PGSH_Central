using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Create;

internal sealed class CreateServiceCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<CreateServiceCommand, int>
{
    public async Task<Result<int>> Handle(CreateServiceCommand request, CancellationToken cancellationToken)
    {
        var hospitalExists = await dbContext.Hospitals.AnyAsync(h => h.Id == request.HospitalId, cancellationToken);
        if (!hospitalExists)
        {
            return Result.Failure<int>(Error.NotFound("Hospitals.NotFound", "The target hospital does not exist."));
        }

        bool nameExists = await dbContext.Services.AnyAsync(s =>
            s.HospitalId == request.HospitalId &&
            s.Name.ToLower() == request.Name.ToLower(), cancellationToken);

        if (nameExists)
        {
            return Result.Failure<int>(Error.Conflict("Services.DuplicateName", "This service already exists in this hospital."));
        }

        var service = new Service
        {
            HospitalId = request.HospitalId,
            Name = request.Name,
            ServiceType = request.ServiceType,
            Capacity = request.Capacity,
            Description = request.Description,
            Specialty = request.Specialty,
        };

        dbContext.Services.Add(service);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(service.Id);
    }
}
