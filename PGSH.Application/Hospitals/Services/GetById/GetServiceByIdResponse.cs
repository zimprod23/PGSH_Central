namespace PGSH.Application.Hospitals.Services.GetById;

/// <summary>
/// <paramref name="LocalizationX"/> / <paramref name="LocalizationY"/> / <paramref name="LocalizationZ"/>
/// are the service's own coordinates when it has them and the hospital's otherwise, so a map never
/// plots nothing. <paramref name="HasOwnLocalization"/> says which of the two it is, because the edit
/// form must not present an inherited position as one the service stated — saving it back would turn
/// a fallback into a fact.
/// </summary>
public record ServiceDetailResponse(
    int Id,
    string Name,
    string Description,
    string ServiceType,
    string? Specialty,
    int Capacity,
    int HospitalId,
    string HospitalName,
    string HospitalCity,
    string? HospitalDescription,
    string? LocalizationX,
    string? LocalizationY,
    string? LocalizationZ,
    bool HasOwnLocalization,
    ServiceChefResponse? ServiceChef,
    List<ServiceLevelCapacityResponse> LevelCapacities,
    List<StaffMemberResponse> Staff);

/// <summary>One authored intake rule. An empty list on the detail means the service takes every promotion.</summary>
public record ServiceLevelCapacityResponse(
    int LevelId,
    string? LevelLabel,
    int LevelYear,
    string AcademicProgram,
    int Capacity);

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
