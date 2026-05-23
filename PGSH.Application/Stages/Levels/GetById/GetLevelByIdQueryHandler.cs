using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Levels.GetById;

internal sealed class GetLevelByIdQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetLevelByIdQuery, LevelResponse>
{
    public async Task<Result<LevelResponse>> Handle(GetLevelByIdQuery request, CancellationToken cancellationToken)
    {
        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.Id)
            .Select(l => new LevelResponse(l.Id, l.Label, l.Year, l.AcademicProgram.ToString()))
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
            return Result.Failure<LevelResponse>(Error.NotFound("Level.NotFound", $"Level {request.Id} not found."));

        return Result.Success(level);
    }
}
