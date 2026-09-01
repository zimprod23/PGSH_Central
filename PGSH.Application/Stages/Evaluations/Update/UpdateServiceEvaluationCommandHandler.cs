using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.Update;

internal sealed class UpdateServiceEvaluationCommandHandler(
    IApplicationDbContext dbContext,
    EvaluationObjectiveResolver objectiveResolver,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<UpdateServiceEvaluationCommand>
{
    public async Task<Result> Handle(
        UpdateServiceEvaluationCommand request, CancellationToken cancellationToken)
    {
        var access = await authorizer.EnsureCanActOnEvaluationAsync(request.EvaluationId, cancellationToken);
        if (access.IsFailure)
            return access;

        var periodId = await dbContext.ServiceEvaluation
            .AsNoTracking()
            .Where(e => e.Id == request.EvaluationId)
            .Select(e => (Guid?)e.ServicePeriodId)
            .FirstOrDefaultAsync(cancellationToken);

        if (periodId is null)
            return Result.Failure(StageErrors.EvaluationNotFound(request.EvaluationId));

        var assignment = await dbContext.InternshipAssignments
            .Include(a => a.Cohort)
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.Evaluation)
                    .ThenInclude(e => e!.ObjectiveScores)
                        .ThenInclude(o => o.StageObjective)
            .FirstOrDefaultAsync(
                a => a.ServicePeriods.Any(p => p.Id == periodId.Value),
                cancellationToken);

        if (assignment is null)
            return Result.Failure(StageErrors.PeriodNotFound(periodId.Value));

        var objectives = await objectiveResolver.ResolveAsync(
            assignment.Cohort.StageId,
            request.ObjectiveScores.Select(o => o.StageObjectiveId),
            cancellationToken);

        if (objectives.IsFailure)
            return Result.Failure(objectives.Error);

        var amendment = new EvaluationAmendment(
            request.Mode,
            request.TotalScore,
            request.Outcome,
            request.SupervisorComment,
            request.FicheReference,
            await authorizer.CurrentUserIdAsync(cancellationToken),
            DateTime.UtcNow,
            request.ObjectiveScores
                .Select(o => new ObjectiveScore
                {
                    StageObjectiveId = o.StageObjectiveId,
                    Score            = o.Score,
                    Outcome          = o.Outcome,
                    Note             = o.Note,
                    StageObjective   = objectives.Value[o.StageObjectiveId],
                })
                .ToList());

        var result = assignment.AmendEvaluation(periodId.Value, amendment);
        if (result.IsFailure)
            return result;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
