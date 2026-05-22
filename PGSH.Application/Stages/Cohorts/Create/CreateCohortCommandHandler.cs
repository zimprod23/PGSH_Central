using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Create;

internal sealed class CreateCohortCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateCohortCommand, int>
{
    public async Task<Result<int>> Handle(CreateCohortCommand request, CancellationToken cancellationToken)
    {
        bool stageExists = await dbContext.Stages
            .AnyAsync(s => s.Id == request.StageId, cancellationToken);
        if (!stageExists)
            return Result.Failure<int>(StageErrors.NotFound(request.StageId));

        bool groupExists = await dbContext.AcademicGroups
            .AnyAsync(g => g.Id == request.AcademicGroupId, cancellationToken);
        if (!groupExists)
            return Result.Failure<int>(Error.NotFound(
                "AcademicGroups.NotFound",
                $"The academic group with Id = '{request.AcademicGroupId}' was not found."));

        var cohort = new Cohort
        {
            StageId         = request.StageId,
            AcademicGroupId = request.AcademicGroupId,
            Label           = request.Label,
        };

        dbContext.Cohorts.Add(cohort);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(cohort.Id);
    }
}
