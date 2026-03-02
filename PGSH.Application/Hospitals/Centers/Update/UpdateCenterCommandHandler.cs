using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Centers.Update;

internal sealed class UpdateCenterCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<UpdateCenterCommand>
{
    public async Task<Result> Handle(UpdateCenterCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch the existing entity
        var center = await dbContext.Centers
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (center is null)
        {
            return Result.Failure(Error.NotFound("Centers.NotFound", $"Center {request.Id} not found."));
        }

        // 2. Uniqueness check: Name must not be taken by ANOTHER center
        bool nameExists = await dbContext.Centers
            .AnyAsync(c => c.Name.ToLower() == request.Name.ToLower() && c.Id != request.Id, cancellationToken);

        if (nameExists)
        {
            return Result.Failure(Error.Conflict("Centers.DuplicateName", "This name is already in use."));
        }

        // 3. Update Properties
        center.Name = request.Name;
        center.CenterType = request.CenterType;
        center.City = request.City;

        // Update Localization (Value Object / Owned Type)
        center.LocalisationMaps = (request.LocalizationX is not null || request.LocalizationY is not null)
            ? new Localization(request.LocalizationX, request.LocalizationY, request.LocalizationZ)
            : null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
