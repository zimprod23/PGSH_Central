using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Create;

internal sealed class CreateStageCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<CreateStageCommand, int>
{
    public async Task<Result<int>> Handle(CreateStageCommand request, CancellationToken cancellationToken)
    {
        bool levelExists = await dbContext.Levels.AnyAsync(l => l.Id == request.LevelId, cancellationToken);
        if(levelExists) return Result.Failure<int>(StageErrors.MissingLevel);

        //Mapping stage
        var stage = new Stage
        {
            Name = request.Name,
            Coefficient = request.Coefficient,
            Description = request.Description,
            DurationInDays = request.DurationInDays,
            //Mapping objectives
            Objectives = request.Objectives.Select(o => new StageObjective
            {
                Label = o.Label,
                Description = o.Description,
                Weight = o.Weight,
                IsMandatory = o.IsMandatory
            }).ToList()
        };

        dbContext.Stages.Add(stage);
        await dbContext.SaveChangesAsync(cancellationToken);
        return stage.Id;
    }
}
