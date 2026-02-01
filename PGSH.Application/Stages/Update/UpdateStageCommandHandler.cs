using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Update;

internal class UpdateStageCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<UpdateStageCommand>
{
    public async Task<Result> Handle(UpdateStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
                                .Include(s => s.Objectives)
                                .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (stage == null) return Result.Failure(StageErrors.NotFound(request.Id));

        if (stage.LevelId != request.LevelId)
        {
            if (!await dbContext.Levels.AnyAsync(l => l.Id == request.LevelId, cancellationToken))
                return Result.Failure(StageErrors.MissingLevel);
        }

        // 3. Update main properties
        stage.Name = request.Name;
        stage.Description = request.Description;
        stage.Coefficient = request.Coefficient;
        stage.DurationInDays = request.DurationInDays;
        stage.LevelId = request.LevelId;

        // 4. Update Objectives (Optimal strategy: Replace the collection)
        stage.Objectives.Clear();
        ((List<StageObjective>)stage.Objectives).AddRange(request.Objectives.Select(o => new StageObjective
        {
            Label = o.Label,
            Description = o.Description,
            Weight = o.Weight,
            IsMandatory = o.IsMandatory,
            StageId = stage.Id
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
