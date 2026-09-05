using PGSH.Application.Hospitals.Chefs;

namespace PGSH.Application.Hospitals.Services.GetMany;

/// <summary>
/// One row of the services list.
/// </summary>
/// <param name="RestrictedLevelCount">
/// 0 means the service carries no intake rules and takes every promotion up to
/// <paramref name="Capacity"/> — not that it is unconfigured.
/// </param>
/// <param name="ServiceChefName">
/// The employee <b>linked</b> to this service through <c>Service.ServiceChefId</c> — configuration,
/// not the name to print. ⚠ It is null on all 148 services of the base, which is why a column bound
/// to it read « — » on every row while the répartition named a chef for 140 of them. Kept because
/// the list can be filtered by that link (<c>ServiceChefId</c>), and « qui est rattaché ? » is a
/// real question; it is simply not the same question as « qui dirige ? ».
/// </param>
/// <param name="ChefAttribution">
/// Who PGSH <b>names</b> as the chef today, resolved by <see cref="ServiceChefDirectory"/> — the
/// same rule the fiche, the répartition and the stage export print. This is what a column headed
/// « Chef de service » shows.
/// </param>
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
    int StaffCount,
    ServiceChefAttributionResponse ChefAttribution);
