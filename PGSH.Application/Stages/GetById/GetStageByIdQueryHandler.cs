using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Students.GetById;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.GetById;

internal sealed class GetStageByIdQueryHandler(
    IApplicationDbContext dbContext) : IQueryHandler<GetStageByIdQuery, StageResponse>
{
    public async Task<Result<StageResponse>> Handle(GetStageByIdQuery request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
                        .AsNoTracking()
                        .Include(s => s.Level)
                        .Include(s => s.Objectives)
                        .Where(s => s.Id == request.StageId)
                        .Select(s => new StageResponse(
                            s.Id,
                            s.Name,
                            s.Coefficient,
                            s.Description,
                            s.DurationInDays,
                            new LevelResponse(
                                s.Level.Label,
                                s.Level.Year,
                                s.Level.AcademicProgram.ToString()
                                ),
                            s.Objectives
                                .OrderByDescending(o => o.Weight)
                                .Select(o => new StageObjectiveResponse(
                                    o.Label,
                                    o.Description,
                                    o.Weight,
                                    o.IsMandatory
                                    ))
                                .ToArray()
                            ))
                        .FirstOrDefaultAsync(cancellationToken);

        if(stage is null) return Result.Failure<StageResponse>(StageErrors.NotFound(request.StageId));

        return stage;
    }
}
