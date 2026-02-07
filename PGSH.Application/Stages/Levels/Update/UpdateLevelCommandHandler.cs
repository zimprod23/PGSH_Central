using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Levels.Update;

internal sealed class UpdateLevelCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<UpdateLevelCommand>
{
    public async Task<Result> Handle(UpdateLevelCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch the level
        var level = await dbContext.Levels
            .FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken);

        if (level is null)
            return Result.Failure(Error.NotFound("Level.NotFound", "Level not found."));

        // 2. Check for duplicate label/year in other records (Performance: Index-backed check)
        bool alreadyExists = await dbContext.Levels
            .AnyAsync(l => l.Id != request.Id &&
                           l.Label == request.Label &&
                           l.Year == request.Year, cancellationToken);

        if (alreadyExists)
            return Result.Failure(Error.Conflict("Level.Duplicate", "Another level with this name and year already exists."));

        // 3. Apply changes
        level.Label = request.Label;
        level.Year = request.Year;
        level.AcademicProgram = (AcademicProgram)request.AcademicProgram;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
