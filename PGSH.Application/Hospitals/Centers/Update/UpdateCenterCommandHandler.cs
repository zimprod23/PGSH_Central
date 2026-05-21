using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Centers.Update;

internal sealed class UpdateCenterCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UpdateCenterCommand>
{
    public async Task<Result> Handle(UpdateCenterCommand request, CancellationToken cancellationToken)
    {
        var center = await dbContext.Centers.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (center is null)
            return Result.Failure(Error.NotFound("Centers.NotFound", $"Center {request.Id} not found."));

        bool nameExists = await dbContext.Centers.AnyAsync(
            c => c.Name.ToLower() == request.Name.ToLower() && c.Id != request.Id, cancellationToken);
        if (nameExists)
            return Result.Failure(Error.Conflict("Centers.DuplicateName", "This name is already in use."));

        center.Name = request.Name;
        center.CenterType = request.CenterType;
        center.City = request.City;
        center.LocalisationMaps = LocalizationMapper.FromCoordinates(request.LocalizationX, request.LocalizationY, request.LocalizationZ);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
