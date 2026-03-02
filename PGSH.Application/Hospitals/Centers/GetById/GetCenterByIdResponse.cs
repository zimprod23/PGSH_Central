namespace PGSH.Application.Hospitals.Centers.GetById;

public record CenterDetailResponse(
    int Id,
    string Name,
    string CenterType,
    string? City,
    string? LocalizationX,
    string? LocalizationY,
    string? LocalizationZ,
    List<HospitalSummaryResponse> Hospitals);

public record HospitalSummaryResponse(
    int Id,
    string Name,
    string City,
    string HospitalType);