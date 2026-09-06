using PGSH.Application.Hospitals.Chefs;

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
    /// <summary>
    /// Whether « autoriser le dépassement d'effectif » may lift this service's number when a plan is
    /// published. True on every service nobody has restricted, which is all 148 of the imported ones.
    /// ⚠ The edit form writes this back, so it has to be here: a summary that fed the form without it
    /// is exactly how a description column got erased once.
    /// </summary>
    bool AllowsOverCapacity,
    /// <summary>
    /// True for a place the faculty does not run. Students reach it only through a délocalisation:
    /// it cannot be authorised on a stage, nobody is arranged into it, and its number never enters
    /// the saturation maths. See <c>Service.IsExternal</c>.
    /// </summary>
    bool IsExternal,
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
    List<StaffMemberResponse> Staff,
    /// <summary>
    /// Every tenure, newest first — who led the service and when. This is what lets a répartition
    /// reprinted three years later name the chef it was published under, and it is the only dated
    /// answer of the three the résolution order considers.
    /// </summary>
    List<ChefTenureResponse> ChefHistory,
    /// <summary>
    /// The name in the legacy « Responsable (source) » note, when the service has one — 140 of 148
    /// services do, and none of those has a configured chef. ⚠ Undated: it says who the Access base
    /// last recorded, not who led the service on any particular date, so it is surfaced separately
    /// rather than folded into <see cref="ServiceChef"/>. Linking a real chef is what replaces it.
    ///
    /// <para>This is the <em>raw</em> fact — what the fiche says. Who PGSH actually <b>names</b> as
    /// this service's chef is <see cref="ChefAttribution"/>, and a screen prints that one.</para>
    /// </summary>
    string? ChefFromSourceNote,
    /// <summary>
    /// Who PGSH names as this service's chef **today**, and on what authority — resolved by
    /// <c>ServiceChefDirectory</c>, the same rule the répartition and the stage export print.
    /// </summary>
    ServiceChefAttributionResponse ChefAttribution);

public record ChefTenureResponse(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string Grade,
    DateOnly StartDate,
    /// <summary>Null while the tenure is the sitting one.</summary>
    DateOnly? EndDate);

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
