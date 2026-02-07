using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Levels.Create;

public sealed class CreateLevelCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<CreateLevelCommand, int>
{
    async Task<Result<int>> IRequestHandler<CreateLevelCommand, Result<int>>.Handle(CreateLevelCommand request, CancellationToken cancellationToken)
    {
        // 1. Performance check: Does this level already exist?
        bool exists = await dbContext.Levels
            .AnyAsync(l => l.Label == request.Label && l.Year == request.Year, cancellationToken);

        if (exists)
        {
            return Result.Failure<int>(Error.Conflict("Level.AlreadyExists", "This level already exists."));
        }

        // 2. Map and Create
        var level = new Level
        {
            Label = request.Label,
            Year = request.Year,
            AcademicProgram = (AcademicProgram)request.AcademicProgram
        };

        dbContext.Levels.Add(level);

        await dbContext.SaveChangesAsync(cancellationToken);

        return level.Id;
    }
}
