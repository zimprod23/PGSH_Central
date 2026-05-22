using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Evaluations.Create;

public sealed record CreateServiceEvaluationCommand(
    Guid ServicePeriodId,
    decimal TotalScore,
    string? SupervisorComment,
    List<ObjectiveScoreRequest> ObjectiveScores) : ICommand<Guid>;

public sealed record ObjectiveScoreRequest(int StageObjectiveId, int Score, string? Note);
