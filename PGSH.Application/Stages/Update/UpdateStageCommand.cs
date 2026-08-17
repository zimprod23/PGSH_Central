using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Update;

public sealed record UpdateStageCommand(
    int Id,
    string Name,
    int Coefficient,
    string? Description,
    int DurationInDays,
    int LevelId,
    List<UpdateStageObjectiveRequest> Objectives,
    // Deliberately NOT optional. A default here let the endpoint's Request record omit the field and
    // still compile, so every save silently reverted the stage to PerPeriod. A PUT re-states the whole
    // stage; the compiler should force each caller to say what it is re-stating.
    StageRotationMode RotationMode) : ICommand;

public sealed record UpdateStageObjectiveRequest(
    string Label,
    string? Description,
    int Weight,
    bool IsMandatory);
