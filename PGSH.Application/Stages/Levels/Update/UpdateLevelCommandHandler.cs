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
        var level = await dbContext.Levels
            .FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken);

        if (level is null)
            return Result.Failure(Error.NotFound("Level.NotFound", "Level not found."));

        bool alreadyExists = await dbContext.Levels
            .AnyAsync(l => l.Id != request.Id &&
                           l.Year == request.Year &&
                           l.AcademicProgram == request.AcademicProgram, cancellationToken);

        if (alreadyExists)
            return Result.Failure(Error.Conflict("Level.Duplicate",
                $"A level for Year {request.Year} in {request.AcademicProgram} already exists."));

        level.Label = request.Label;
        level.Year = request.Year;
        level.AcademicProgram = request.AcademicProgram;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
