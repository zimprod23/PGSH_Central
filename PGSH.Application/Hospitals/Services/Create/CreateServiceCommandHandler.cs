using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Create;

internal sealed class CreateServiceCommandHandler(
    IApplicationDbContext dbContext,
    ServiceLevelCapacityResolver capacityResolver) : ICommandHandler<CreateServiceCommand, int>
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
            return Result.Failure<int>(ServiceErrors.DuplicateName);
        }

        var quotas = await capacityResolver.ResolveAsync(request.LevelCapacities, cancellationToken);
        if (quotas.IsFailure)
            return Result.Failure<int>(quotas.Error);

        var service = new Service
        {
            HospitalId = request.HospitalId,
            Name = request.Name,
            ServiceType = request.ServiceType,
            Capacity = request.Capacity,
            Description = request.Description,
            Specialty = request.Specialty,
            LocalisationMaps = LocalizationMapper.FromCoordinates(
                request.LocalizationX, request.LocalizationY, request.LocalizationZ),
        };

        service.ReplaceLevelCapacities(quotas.Value);

        // Add on a brand-new graph: EF marks the whole tree Added, so the quotas' store-generated
        // keys are safe here in a way they would not be on an already-tracked service.
        dbContext.Services.Add(service);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(service.Id);
    }
}
