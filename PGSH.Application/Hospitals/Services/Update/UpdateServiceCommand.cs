using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Services.Update;

public record UpdateServiceCommand(
    int Id,
    string Name,
    string Description,
    ServiceType ServiceType,
    int Capacity,
    int HospitalId,
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
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Null means « unchanged », not « interne ».</b> This is a full-replace command, so an older
    /// client saving an external service without the field would quietly bring it back into the
    /// rotation and into the saturation maths — with students already délocalisés on it. Unlike every
    /// other field here that is not a value the caller can be assumed to hold an opinion about.
    /// </remarks>
    bool? IsExternal = null) : ICommand;
