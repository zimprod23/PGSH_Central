namespace PGSH.Application.Stages.GetMany;

public sealed record StageSummaryResponse(
    int Id,
    string Name,
    int Coefficient,
    int DurationInDays,
    string LevelLabel);