using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Evaluations.Create;

public sealed record CreateServiceEvaluationCommand(
    Guid ServicePeriodId,
    EvaluationMode Mode,
    decimal? TotalScore,
    EvaluationOutcome? Outcome,
    string? SupervisorComment,
    List<ObjectiveScoreRequest> ObjectiveScores) : ICommand<Guid>;

public sealed record ObjectiveScoreRequest(int StageObjectiveId, int? Score, EvaluationOutcome? Outcome, string? Note);
