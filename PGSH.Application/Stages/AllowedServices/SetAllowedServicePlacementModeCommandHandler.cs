using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.AllowedServices;

internal sealed class SetAllowedServicePlacementModeCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<SetAllowedServicePlacementModeCommand>
{
    public async Task<Result> Handle(
        SetAllowedServicePlacementModeCommand request, CancellationToken cancellationToken)
    {
        var authorisation = await dbContext.StageAllowedServices
            .FirstOrDefaultAsync(
                a => a.StageId == request.StageId && a.ServiceId == request.ServiceId,
                cancellationToken);

        // ⚠ Three refusals rather than one, because they send the reader to three different places:
        // the stage does not exist, the service does not exist, or the service exists and this stage
        // does not authorise it — the last being the ordinary case (somebody reserved a service on
        // the wrong stage's page) and the only one whose fix is « authorise it first ».
        if (authorisation is null)
        {
            bool stageExists = await dbContext.Stages
                .AnyAsync(s => s.Id == request.StageId, cancellationToken);

            if (!stageExists)
                return Result.Failure(StageErrors.NotFound(request.StageId));

            bool serviceExists = await dbContext.Services
                .AnyAsync(s => s.Id == request.ServiceId, cancellationToken);

            return Result.Failure(serviceExists
                ? StageErrors.ServiceNotAllowedInStage(request.ServiceId, request.StageId)
                : ServiceErrors.NotFound(request.ServiceId));
        }

        if (authorisation.PlacementMode == request.PlacementMode)
            return Result.Success();

        authorisation.PlacementMode = request.PlacementMode;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
