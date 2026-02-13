using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delete;

internal sealed class DeleteStageCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<DeleteStageCommand>
{
    public async Task<Result> Handle(DeleteStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages.FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken);

        if (stage is null) return Result.Failure(StageErrors.NotFound(request.StageId));

        dbContext.Stages.Remove(stage);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();    

    }
}