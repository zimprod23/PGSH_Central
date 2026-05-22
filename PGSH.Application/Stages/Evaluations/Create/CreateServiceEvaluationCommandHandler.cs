using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.Create;

internal sealed class CreateServiceEvaluationCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateServiceEvaluationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateServiceEvaluationCommand request, CancellationToken cancellationToken)
    {
        var assignment = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.Evaluation)
                    .ThenInclude(e => e!.ObjectiveScores)
                        .ThenInclude(o => o.StageObjective)
            .FirstOrDefaultAsync(
                a => a.ServicePeriods.Any(p => p.Id == request.ServicePeriodId),
                cancellationToken);

        if (assignment is null)
            return Result.Failure<Guid>(StageErrors.PeriodNotFound(request.ServicePeriodId));

        var evaluation = new ServiceEvaluation
        {
            Id                = Guid.NewGuid(),
            ServicePeriodId   = request.ServicePeriodId,
            TotalScore        = request.TotalScore,
            SupervisorComment = request.SupervisorComment,
            ObjectiveScores   = request.ObjectiveScores
                .Select(o => new ObjectiveScore
                {
                    Id               = Guid.NewGuid(),
                    StageObjectiveId = o.StageObjectiveId,
                    Score            = o.Score,
                    Note             = o.Note,
                })
                .ToList(),
        };

        var result = assignment.SubmitEvaluation(request.ServicePeriodId, evaluation);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        dbContext.ServiceEvaluation.Add(evaluation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(evaluation.Id);
    }
}
