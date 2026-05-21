using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Create;

internal sealed class CreateHospitalCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateHospitalCommand, int>
{
    public async Task<Result<int>> Handle(CreateHospitalCommand request, CancellationToken cancellationToken)
    {
        var centerExists = await dbContext.Centers.AnyAsync(c => c.Id == request.CenterId, cancellationToken);
        if (!centerExists)
            return Result.Failure<int>(Error.NotFound("Centers.NotFound", "The specified Center does not exist."));

        bool duplicate = await dbContext.Hospitals.AnyAsync(h =>
            h.Name.ToLower() == request.Name.ToLower() &&
            h.City.ToLower() == request.City.ToLower(), cancellationToken);

        if (duplicate)
            return Result.Failure<int>(Error.Conflict("Hospitals.Duplicate", "A hospital with this name already exists in this city."));

        var hospital = new Hospital
        {
            CenterId = request.CenterId,
            Name = request.Name,
            HospitalType = request.HospitalType,
            City = request.City,
            Description = request.Description,
            Email = request.Email,
            LocalisationMaps = LocalizationMapper.FromCoordinates(request.LocalizationX, request.LocalizationY, request.LocalizationZ)
        };

        dbContext.Hospitals.Add(hospital);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(hospital.Id);
    }
}
