using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Evaluations.Update;

public sealed record UpdateServiceEvaluationCommand(
    Guid EvaluationId,
    EvaluationMode Mode,
    decimal? TotalScore,
    EvaluationOutcome? Outcome,
    string? SupervisorComment,
    List<UpdateObjectiveScoreDto> ObjectiveScores) : ICommand;

public sealed record UpdateObjectiveScoreDto(int StageObjectiveId, int? Score, EvaluationOutcome? Outcome, string? Note);
