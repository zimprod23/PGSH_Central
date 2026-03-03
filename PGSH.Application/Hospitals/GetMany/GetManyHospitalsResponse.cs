namespace PGSH.Application.Hospitals.GetMany;

public record HospitalSummaryResponse(
    int Id,
    string Name,
    int CenterId,
    string CenterName,
    string HospitalType,
    string City,
    string? Email);