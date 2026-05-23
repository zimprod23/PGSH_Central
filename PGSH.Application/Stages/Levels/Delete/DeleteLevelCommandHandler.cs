using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Levels.Delete;

internal sealed class DeleteLevelCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<DeleteLevelCommand>
{
    public async Task<Result> Handle(DeleteLevelCommand request, CancellationToken cancellationToken)
    {
        var level = await dbContext.Levels
            .FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken);

        if (level is null)
            return Result.Failure(Error.NotFound("Level.NotFound", $"Level {request.Id} not found."));

        dbContext.Levels.Remove(level);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
