using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Centers.Create;

internal sealed class CreateCenterCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateCenterCommand, int>
{
    public async Task<Result<int>> Handle(CreateCenterCommand request, CancellationToken cancellationToken)
    {
        bool exists = await dbContext.Centers.AnyAsync(c => c.Name.ToLower() == request.Name.ToLower(), cancellationToken);
        if (exists)
            return Result.Failure<int>(Error.Conflict("Centers.DuplicateName", $"A center with the name '{request.Name}' already exists."));

        var center = new Center
        {
            Name = request.Name,
            CenterType = request.CenterType,
            City = request.City,
            LocalisationMaps = LocalizationMapper.FromCoordinates(request.LocalizationX, request.LocalizationY, request.LocalizationZ)
        };

        dbContext.Centers.Add(center);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(center.Id);
    }
}
