namespace PGSH.Application.Hospitals.GetById;

public record HospitalDetailResponse(
    int Id,
    string Name,
    int CenterId,
    string CenterName,
    string HospitalType,
    string City,
    string? Description,
    string? Email,
    string? LocalizationX,
    string? LocalizationY,
    List<ServiceSummaryResponse> Services);

public record ServiceSummaryResponse(
    int Id,
    string Name,
    int Capacity,
    string? ServiceChefName);