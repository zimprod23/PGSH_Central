using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.Create;

internal sealed class CreateServiceEvaluationCommandHandler(
    IApplicationDbContext dbContext,
    EvaluationObjectiveResolver objectiveResolver,
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
            .Include(a => a.Cohort)
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.Evaluation)
                    .ThenInclude(e => e!.ObjectiveScores)
                        .ThenInclude(o => o.StageObjective)
            .FirstOrDefaultAsync(
                a => a.ServicePeriods.Any(p => p.Id == request.ServicePeriodId),
                cancellationToken);

        if (assignment is null)
            return Result.Failure<Guid>(StageErrors.PeriodNotFound(request.ServicePeriodId));

        var objectives = await objectiveResolver.ResolveAsync(
            assignment.Cohort.StageId,
            request.ObjectiveScores.Select(o => o.StageObjectiveId),
            cancellationToken);

        if (objectives.IsFailure)
            return Result.Failure<Guid>(objectives.Error);

        var evaluation = new ServiceEvaluation
        {
            Id                = Guid.NewGuid(),
            ServicePeriodId   = request.ServicePeriodId,
            Mode              = request.Mode,
            TotalScore        = request.TotalScore,
            Outcome           = request.Outcome,
            SupervisorComment = request.SupervisorComment,
            FicheReference    = request.FicheReference,
            EvaluatedByUserId = await authorizer.CurrentUserIdAsync(cancellationToken),
            EvaluatedAt       = DateTime.UtcNow,
            ObjectiveScores   = request.ObjectiveScores
                .Select(o => new ObjectiveScore
                {
                    StageObjectiveId = o.StageObjectiveId,
                    Score            = o.Score,
                    Outcome          = o.Outcome,
                    Note             = o.Note,
                    StageObjective   = objectives.Value[o.StageObjectiveId],
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
