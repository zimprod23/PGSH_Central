namespace PGSH.Application.Hospitals.Services.GetMany;

public record ServiceSummaryResponse(
    int Id,
    string Name,
    string ServiceType,
    int Capacity,
    int HospitalId,
    string HospitalName,
    string? ServiceChefName,
    int StaffCount);