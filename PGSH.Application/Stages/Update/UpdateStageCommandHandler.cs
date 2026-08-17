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

        // The mode is what shaped the periods already on disk — one per cell, or one per run — so
        // flipping it under a published répartition leaves the arranger and the publisher describing
        // a rotation that does not match the one students were sent on. Unpublish first.
        if (stage.RotationMode != request.RotationMode)
        {
            bool published = await dbContext.ServicePeriodSlotCoverage
                .AnyAsync(c => c.CohortSlotAssignment.StageSlot.StageId == request.Id, cancellationToken);

            if (published)
                return Result.Failure(StageErrors.RotationModeLockedByPublication(stage.Name));
        }

        stage.Name = request.Name;
        stage.Description = request.Description;
        stage.Coefficient = request.Coefficient;
        stage.DurationInDays = request.DurationInDays;
        stage.RotationMode = request.RotationMode;
        stage.LevelId = request.LevelId;

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
