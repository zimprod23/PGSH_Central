using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Update;

internal sealed class UpdateServiceCommandHandler(
    IApplicationDbContext dbContext,
    ServiceLevelCapacityResolver capacityResolver) : ICommandHandler<UpdateServiceCommand>
{
    public async Task<Result> Handle(UpdateServiceCommand request, CancellationToken cancellationToken)
    {
        var service = await dbContext.Services
            .Include(s => s.LevelCapacities)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (service is null)
            return Result.Failure(ServiceErrors.NotFound(request.Id));

        if (service.HospitalId != request.HospitalId)
        {
            var hospitalExists = await dbContext.Hospitals.AnyAsync(h => h.Id == request.HospitalId, cancellationToken);
            if (!hospitalExists)
                return Result.Failure(Error.NotFound("Hospitals.NotFound", "Target Hospital not found."));

            service.HospitalId = request.HospitalId;
        }
        bool nameExists = await dbContext.Services.AnyAsync(s =>
            s.Id != request.Id &&
            s.HospitalId == request.HospitalId &&
            s.Name.ToLower() == request.Name.ToLower(), cancellationToken);

        if (nameExists)
            return Result.Failure(ServiceErrors.DuplicateName);

        var quotas = await capacityResolver.ResolveAsync(request.LevelCapacities, cancellationToken);
        if (quotas.IsFailure)
            return Result.Failure(quotas.Error);

        service.Name = request.Name;
        service.Description = request.Description;
        service.Specialty = request.Specialty;
        service.ServiceType = request.ServiceType;
        service.Capacity = request.Capacity;
        service.LocalisationMaps = LocalizationMapper.FromCoordinates(
            request.LocalizationX, request.LocalizationY, request.LocalizationZ);

        service.ReplaceLevelCapacities(quotas.Value);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
