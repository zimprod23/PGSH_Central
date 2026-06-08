using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.Update;

internal sealed class UpdateServiceEvaluationCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<UpdateServiceEvaluationCommand>
{
    public async Task<Result> Handle(
        UpdateServiceEvaluationCommand request, CancellationToken cancellationToken)
    {
        var access = await authorizer.EnsureCanActOnEvaluationAsync(request.EvaluationId, cancellationToken);
        if (access.IsFailure)
            return access;

        var evaluation = await dbContext.ServiceEvaluation
            .Include(e => e.ObjectiveScores)
            .FirstOrDefaultAsync(e => e.Id == request.EvaluationId, cancellationToken);

        if (evaluation is null)
            return Result.Failure(StageErrors.EvaluationNotFound(request.EvaluationId));

        var assignment = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.Evaluation)
                    .ThenInclude(e => e!.ObjectiveScores)
                        .ThenInclude(o => o.StageObjective)
            .FirstOrDefaultAsync(
                a => a.ServicePeriods.Any(p => p.Id == evaluation.ServicePeriodId),
                cancellationToken);

        if (assignment is null)
            return Result.Failure(StageErrors.PeriodNotFound(evaluation.ServicePeriodId));

        if (assignment.Status is InternshipStatus.Validated or InternshipStatus.Rejected)
            return Result.Failure(StageErrors.EvaluationReadOnly(assignment.Status));

        var objectiveIds = request.ObjectiveScores.Select(o => o.StageObjectiveId).Distinct().ToList();
        var stageObjectives = await dbContext.StageObjectives
            .Where(o => objectiveIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        evaluation.Mode              = request.Mode;
        evaluation.TotalScore        = request.TotalScore;
        evaluation.Outcome           = request.Outcome;
        evaluation.SupervisorComment = request.SupervisorComment;

        dbContext.ObjectiveScores.RemoveRange(evaluation.ObjectiveScores);

        evaluation.ObjectiveScores = request.ObjectiveScores
            .Select(o => new ObjectiveScore
            {
                Id                  = Guid.NewGuid(),
                ServiceEvaluationId = evaluation.Id,
                StageObjectiveId    = o.StageObjectiveId,
                Score               = o.Score,
                Outcome             = o.Outcome,
                Note                = o.Note,
                StageObjective      = stageObjectives.GetValueOrDefault(o.StageObjectiveId)!,
            })
            .ToList();

        evaluation.Normalize();
        assignment.RecalculateFinalScore();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
