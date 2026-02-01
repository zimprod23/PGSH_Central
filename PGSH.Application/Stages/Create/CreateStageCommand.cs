using PGSH.Application.Abstractions.Messaging;
using System.Windows.Input;

namespace PGSH.Application.Stages.Create;

public sealed record CreateStageCommand(
    string Name,
    int Coefficient,
    string? Description,
    int DurationInDays,
    int LevelId,
    List<CreateStageObjectiveRequest> Objectives): ICommand<int>;


public sealed record CreateStageObjectiveRequest(
    string Label,
    string? Description,
    int Weight,
    bool IsMandatory);