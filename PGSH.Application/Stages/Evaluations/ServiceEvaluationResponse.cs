namespace PGSH.Application.Stages.Evaluations;

public sealed record ServiceEvaluationResponse(
    Guid Id,
    Guid ServicePeriodId,
    decimal TotalScore,
    string? SupervisorComment,
    IReadOnlyList<ObjectiveScoreResponse> ObjectiveScores);

public sealed record ObjectiveScoreResponse(
    Guid Id,
    int StageObjectiveId,
    string ObjectiveLabel,
    int Weight,
    bool IsMandatory,
    int Score,
    string? Note);
