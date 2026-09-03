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

/// <summary>
/// The resolved answer to « qui dirige ce service ? », sent rather than re-derived.
///
/// <para>⚠ <b>This exists because the screen and the documents disagreed, and it cost a real « d'où
/// sort ce nom ? » on 2026-09-03.</b> The page ranked the sources itself — the sitting FK (null on
/// all 148 services), then the note, with the open tenure filed under « Historique » — while
/// <c>ServiceChefDirectory</c> ranked them the other way. One rule, two sides of a network
/// boundary, nothing able to catch them drifting: the same class as
/// <c>ServicePeriodResponse.State</c>, and the same fix.</para>
/// </summary>
/// <param name="Name">Null when nobody is named at all — « aucun chef désigné ».</param>
/// <param name="FromSourceNote">
/// The name is the <b>undated</b> import note rather than a dated affectation. Never dropped beside
/// the name: printing an undated note as the record is a claim nothing supports.
/// </param>
/// <param name="LinkedChefWithheld">
/// ⚠ A chef <em>is</em> linked in Personnel and is deliberately not the name above — the temporary
/// <c>ServiceChefPolicy.InForce</c> = <c>SourceNoteOnly</c>. Without this the page shows an
/// « en cours » tenure under a headline naming somebody else and explains neither, which is the
/// confusion this whole change removes. False when nobody is linked: that is a different sentence.
/// </param>
public record ServiceChefAttributionResponse(
    string? Name,
    bool FromSourceNote,
    bool LinkedChefWithheld);

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
