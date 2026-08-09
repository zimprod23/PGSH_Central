namespace PGSH.Application.Hospitals.Centers.GetMany;

/// <summary>
/// ⚠ Carries every field the edit form writes back, not only the columns the table renders —
/// see <see cref="PGSH.Application.Hospitals.GetMany.HospitalSummaryResponse"/> for why.
/// </summary>
public record CenterSummaryResponse(
    int Id,
    string Name,
    string CenterType,
    string? City,
    string? X,
    string? Y,
    string? Z);
