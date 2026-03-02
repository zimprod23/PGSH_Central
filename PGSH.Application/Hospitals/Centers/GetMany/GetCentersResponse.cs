namespace PGSH.Application.Hospitals.Centers.GetMany;
public record CenterSummaryResponse(
    int Id,
    string Name,
    string CenterType,
    string? City,
    string? X,
    string? Y);