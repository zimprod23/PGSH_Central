using PGSH.Application.Students.GetById;

namespace PGSH.Application.Stages.GetById;

public sealed record StageResponse(
    int Id,
    string Name,
    int Coefficient,
    string? Description,
    int DurationInDays,
    LevelResponse? LevelResponse,
    StageObjectiveResponse[] StageObjectiveResponse
    );

public record StageObjectiveResponse(
    string Label,
    string? Description,
    int Weight,
    bool IsMandatory
    );


