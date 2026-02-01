using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Update;

public sealed record UpdateStageCommand(
    int Id,
    string Name,
    int Coefficient,
    string? Description,
    int DurationInDays,
    int LevelId,
    List<UpdateStageObjectiveRequest> Objectives) : ICommand;

public sealed record UpdateStageObjectiveRequest(
    string Label,
    string? Description,
    int Weight,
    bool IsMandatory);
