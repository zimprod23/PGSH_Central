using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Evaluations;

public sealed record ServiceEvaluationResponse(
    Guid Id,
    Guid ServicePeriodId,
    EvaluationMode Mode,
    decimal? TotalScore,
    EvaluationOutcome? Outcome,
    string? SupervisorComment,
    IReadOnlyList<ObjectiveScoreResponse> ObjectiveScores);

public sealed record ObjectiveScoreResponse(
    Guid Id,
    int StageObjectiveId,
    string ObjectiveLabel,
    int Weight,
    bool IsMandatory,
    int? Score,
    EvaluationOutcome? Outcome,
    string? Note);
