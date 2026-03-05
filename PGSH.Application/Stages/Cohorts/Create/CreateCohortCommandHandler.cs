using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Create;

internal class CreateCohortCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<CreateCohortCommand, int>
{
    public async Task<Result<int>> Handle(CreateCohortCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify the Stage (Academic Definition) exists
        var stageExists = await dbContext.Stages
            .AnyAsync(s => s.Id == request.StageId, cancellationToken);

        if (!stageExists)
        {
            return Result.Failure<int>(StageErrors.NotFound(request.StageId));
        }

        // 2. Initialize the Cohort
        var cohort = new Cohort
        {
            StageId = request.StageId,
            Label = request.Label,
            RotationTemplates = new List<CohortRotationTemplate>()
        };

        dbContext.Cohorts.Add(cohort);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(cohort.Id);
    }
}
