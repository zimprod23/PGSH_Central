namespace PGSH.Application.Hospitals.Services.GetMany;

/// <summary>
/// <paramref name="RestrictedLevelCount"/> of 0 means the service carries no intake rules and takes
/// every promotion up to <paramref name="Capacity"/> — not that it is unconfigured.
/// </summary>
public record ServiceSummaryResponse(
    int Id,
    string Name,
    string ServiceType,
    string? Specialty,
    int Capacity,
    int RestrictedLevelCount,
    int HospitalId,
    string HospitalName,
    string? ServiceChefName,
    int StaffCount);
