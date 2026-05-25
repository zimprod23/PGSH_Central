using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Evaluations.Update;

public sealed record UpdateServiceEvaluationCommand(
    Guid EvaluationId,
    decimal TotalScore,
    string? SupervisorComment,
    List<UpdateObjectiveScoreDto> ObjectiveScores) : ICommand;

public sealed record UpdateObjectiveScoreDto(int StageObjectiveId, int Score, string? Note);
