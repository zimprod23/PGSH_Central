using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.Create;

internal sealed class CreateServiceEvaluationCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<CreateServiceEvaluationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateServiceEvaluationCommand request, CancellationToken cancellationToken)
    {
        var access = await authorizer.EnsureCanActOnPeriodAsync(request.ServicePeriodId, cancellationToken);
        if (access.IsFailure)
            return Result.Failure<Guid>(access.Error);

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

        // Attach the StageObjective navigation so the weighted FinalScore is computed
        // correctly inside SubmitEvaluation (without it, every objective weighs 1).
        var objectiveIds = request.ObjectiveScores.Select(o => o.StageObjectiveId).Distinct().ToList();
        var stageObjectives = await dbContext.StageObjectives
            .Where(o => objectiveIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var evaluation = new ServiceEvaluation
        {
            Id                = Guid.NewGuid(),
            ServicePeriodId   = request.ServicePeriodId,
            Mode              = request.Mode,
            TotalScore        = request.TotalScore,
            Outcome           = request.Outcome,
            SupervisorComment = request.SupervisorComment,
            ObjectiveScores   = request.ObjectiveScores
                .Select(o => new ObjectiveScore
                {
                    Id               = Guid.NewGuid(),
                    StageObjectiveId = o.StageObjectiveId,
                    Score            = o.Score,
                    Outcome          = o.Outcome,
                    Note             = o.Note,
                    StageObjective   = stageObjectives.GetValueOrDefault(o.StageObjectiveId)!,
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
