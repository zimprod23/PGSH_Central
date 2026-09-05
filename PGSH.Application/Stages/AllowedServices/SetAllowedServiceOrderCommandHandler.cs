using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.AllowedServices;

internal sealed class SetAllowedServiceOrderCommandHandler(
    IApplicationDbContext dbContext,
    ServiceRankWriter rankWriter)
    : ICommandHandler<SetAllowedServiceOrderCommand>
{
    public async Task<Result> Handle(
        SetAllowedServiceOrderCommand request, CancellationToken cancellationToken)
    {
        bool stageExists = await dbContext.Stages
            .AnyAsync(s => s.Id == request.StageId, cancellationToken);

        if (!stageExists)
            return Result.Failure(StageErrors.NotFound(request.StageId));

        var current = await rankWriter.ReadOrderAsync(request.StageId, cancellationToken);

        var order = ServiceRotationOrder.Reorder(current, request.ServiceIdsInOrder);
        if (order.IsFailure)
            return Result.Failure(order.Error);

        return await rankWriter.ApplyAsync(request.StageId, order.Value, cancellationToken);
    }
}
