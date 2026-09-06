using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Services.Create;

public record CreateServiceCommand(
    int HospitalId,
    string Name,
    ServiceType ServiceType,
    int Capacity,
    string Description,
    string? Specialty,
    string? LocalizationX = null,
    string? LocalizationY = null,
    string? LocalizationZ = null,
    IReadOnlyCollection<ServiceLevelCapacityRequest>? LevelCapacities = null,
    /// <summary>
    /// Whether a publication may be forced past the service's number — see
    /// <c>Service.AllowsOverCapacity</c>. <b>Defaults to true</b>, and the default is load-bearing:
    /// a request that says nothing describes a service which has refused nothing, so neither an API
    /// client nor a form that has not been taught about the flag can silently make a service strict.
    /// The other direction — silently re-opening one — is the same exposure every other field of
    /// this full-replace command already carries, and the edit form is fed by the detail response,
    /// which states it.
    /// </summary>
    bool AllowsOverCapacity = true,
    /// <summary>
    /// Whether this is a place the faculty does not run — see <c>Service.IsExternal</c>.
    /// <b>Defaults to false</b>, which is what every service of the CHU is: an external one is a
    /// deliberate act, and a client that has not been taught about the flag must not be able to
    /// create one by omission.
    /// </summary>
    bool IsExternal = false) : ICommand<int>;
