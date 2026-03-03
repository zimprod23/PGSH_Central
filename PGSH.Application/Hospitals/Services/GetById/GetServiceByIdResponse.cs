namespace PGSH.Application.Hospitals.Services.GetById;

public record ServiceDetailResponse(
    int Id,
    string Name,
    string Description,
    string ServiceType,
    int Capacity,
    int HospitalId,
    string HospitalName,
    ServiceChefResponse? ServiceChef,
    List<StaffMemberResponse> Staff);

public record ServiceChefResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string? PPR,
    string Grade); // e.g., "PES", "MC"

public record StaffMemberResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string? PPR,
    string Grade,
    string Position); // e.g., "ServiceChef" or "Normal"