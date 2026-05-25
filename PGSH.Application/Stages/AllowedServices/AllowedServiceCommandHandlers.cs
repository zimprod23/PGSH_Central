using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.AllowedServices;

internal sealed class AddAllowedServiceCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AddAllowedServiceCommand>
{
    public async Task<Result> Handle(AddAllowedServiceCommand request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .Include(s => s.AllowedServices)
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken);

        if (stage is null)
            return Result.Failure(StageErrors.NotFound(request.StageId));

        var service = await dbContext.Services
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (service is null)
            return Result.Failure(Error.NotFound("Services.NotFound", $"Service {request.ServiceId} was not found."));

        if (stage.AllowedServices.Any(s => s.Id == request.ServiceId))
            return Result.Success();

        stage.AllowedServices.Add(service);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class RemoveAllowedServiceCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<RemoveAllowedServiceCommand>
{
    public async Task<Result> Handle(RemoveAllowedServiceCommand request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .Include(s => s.AllowedServices)
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken);

        if (stage is null)
            return Result.Failure(StageErrors.NotFound(request.StageId));

        var service = stage.AllowedServices.FirstOrDefault(s => s.Id == request.ServiceId);
        if (service is null)
            return Result.Success();

        stage.AllowedServices.Remove(service);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
