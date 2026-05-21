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
        bool exists = await dbContext.Levels
            .AnyAsync(l => l.Year == request.Year && l.AcademicProgram == request.AcademicProgram, cancellationToken);

        if (exists)
            return Result.Failure<int>(Error.Conflict("Level.AlreadyExists",
                $"A level for Year {request.Year} in {request.AcademicProgram} already exists."));

        var level = new Level
        {
            Label = request.Label,
            Year = request.Year,
            AcademicProgram = request.AcademicProgram,
        };

        dbContext.Levels.Add(level);

        await dbContext.SaveChangesAsync(cancellationToken);

        return level.Id;
    }
}
