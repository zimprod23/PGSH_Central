using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Update;

internal sealed class UpdateServiceCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<UpdateServiceCommand>
{
    public async Task<Result> Handle(UpdateServiceCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch Service
        var service = await dbContext.Services
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (service is null)
            return Result.Failure(Error.NotFound("Services.NotFound", "Service not found."));

        // 2. Validate Hospital if moving the service
        if (service.HospitalId != request.HospitalId)
        {
            var hospitalExists = await dbContext.Hospitals.AnyAsync(h => h.Id == request.HospitalId, cancellationToken);
            if (!hospitalExists)
                return Result.Failure(Error.NotFound("Hospitals.NotFound", "Target Hospital not found."));

            service.HospitalId = request.HospitalId;
        }
        // 3. Name conflict check (within the same hospital)
        bool nameExists = await dbContext.Services.AnyAsync(s =>
            s.Id != request.Id &&
            s.HospitalId == request.HospitalId &&
            s.Name.ToLower() == request.Name.ToLower(), cancellationToken);

        if (nameExists)
            return Result.Failure(Error.Conflict("Services.DuplicateName", "A service with this name already exists in this hospital."));

        // 4. Update Properties
        service.Name = request.Name;
        service.Description = request.Description;
        service.ServiceType = request.ServiceType;
        service.Capacity = request.Capacity;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
