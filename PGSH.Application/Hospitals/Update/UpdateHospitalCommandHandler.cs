using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Update;

internal sealed class UpdateHospitalCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UpdateHospitalCommand>
{
    public async Task<Result> Handle(UpdateHospitalCommand request, CancellationToken cancellationToken)
    {
        var hospital = await dbContext.Hospitals.FirstOrDefaultAsync(h => h.Id == request.Id, cancellationToken);
        if (hospital is null)
            return Result.Failure(Error.NotFound("Hospitals.NotFound", "Hospital not found."));

        if (hospital.CenterId != request.CenterId)
        {
            var centerExists = await dbContext.Centers.AnyAsync(c => c.Id == request.CenterId, cancellationToken);
            if (!centerExists)
                return Result.Failure(Error.NotFound("Centers.NotFound", "Target Center not found."));

            hospital.CenterId = request.CenterId;
        }

        bool nameExists = await dbContext.Hospitals.AnyAsync(h =>
            h.Id != request.Id &&
            h.Name.ToLower() == request.Name.ToLower() &&
            h.City.ToLower() == request.City.ToLower(), cancellationToken);

        if (nameExists)
            return Result.Failure(Error.Conflict("Hospitals.Duplicate", "Another hospital with this name exists in this city."));

        hospital.Name = request.Name;
        hospital.HospitalType = request.HospitalType;
        hospital.City = request.City;
        hospital.Description = request.Description;
        hospital.Email = request.Email;
        hospital.LocalisationMaps = LocalizationMapper.FromCoordinates(request.LocalizationX, request.LocalizationY, request.LocalizationZ);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
