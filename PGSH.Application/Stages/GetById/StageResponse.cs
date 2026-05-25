using PGSH.Application.Stages.Levels;

namespace PGSH.Application.Stages.GetById;

public sealed record StageResponse(
    int Id,
    string Name,
    int Coefficient,
    string? Description,
    int DurationInDays,
    LevelResponse? LevelResponse,
    StageObjectiveResponse[] StageObjectiveResponse,
    AllowedServiceSummary[] AllowedServices
    );

public sealed record AllowedServiceSummary(int Id, string Name, string HospitalName);

public record StageObjectiveResponse(
    string Label,
    string? Description,
    int Weight,
    bool IsMandatory
    );


