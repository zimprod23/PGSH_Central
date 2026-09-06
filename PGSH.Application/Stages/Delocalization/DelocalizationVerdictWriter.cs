using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Stages.Evaluations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delocalization;

/// <summary>
/// Writes a <see cref="DelocalizationVerdict"/> onto the ad-hoc period a délocalisation created,
/// through the aggregate — so the stage note is rolled up and the submission is raised exactly as it
/// is when a chef enters one.
/// </summary>
/// <remarks>
/// ⚠ Shared by the délocalisation and by the correction that follows it, because the objectives have
/// to be <i>attached</i> and not merely referenced: <c>StageScoring</c> weighs an objective it cannot
/// see as 1, and a fiche whose objectives carry real weights then produces a different mark depending
/// on which handler wrote it.
/// </remarks>
internal sealed class DelocalizationVerdictWriter(
    EvaluationObjectiveResolver objectiveResolver,
    ExecutionAuthorizer authorizer)
{
    public async Task<Result> WriteAsync(
        InternshipAssignment assignment,
        ServicePeriod period,
        int stageId,
        DelocalizationVerdict verdict,
        CancellationToken ct)
    {
        var requested = verdict.ObjectiveScores ?? [];

        var objectives = await objectiveResolver.ResolveAsync(
            stageId, requested.Select(o => o.StageObjectiveId), ct);

        if (objectives.IsFailure)
            return Result.Failure(objectives.Error);

        var evaluation = new ServiceEvaluation
        {
            ServicePeriodId   = period.Id,
            Mode              = verdict.Mode,
            TotalScore        = verdict.TotalScore,
            Outcome           = verdict.Outcome,
            SupervisorComment = verdict.SupervisorComment,
            FicheReference    = verdict.FicheReference,
            // Whoever typed the paper in, not whoever signed it: the external chef has no account
            // here, and leaving this null would read as an evaluation nobody entered.
            EvaluatedByUserId = await authorizer.CurrentUserIdAsync(ct),
            EvaluatedAt       = DateTime.UtcNow,
            ObjectiveScores   = requested
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

        return assignment.SubmitEvaluation(period.Id, evaluation);
    }
}
