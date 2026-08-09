namespace PGSH.Application.Hospitals.Services;

/// <summary>
/// One intake rule as the admin form sends it: this service takes <paramref name="Capacity"/>
/// students of <paramref name="LevelId"/> at once.
///
/// Create and update both take the full set and make the service's rules exactly that — an omitted
/// level is a level the service no longer takes, and an empty list reopens it to everyone. Patch
/// semantics would leave no way to express "no restrictions" at all.
/// </summary>
public sealed record ServiceLevelCapacityRequest(int LevelId, int Capacity);
