using PGSH.SharedKernel;

namespace PGSH.Domain.Hospitals;

public static class ServiceErrors
{
    public static Error NotFound(int id) => Error.NotFound(
        "Services.NotFound", $"Service {id} not found.");

    public static Error DuplicateName => Error.Conflict(
        "Services.DuplicateName", "A service with this name already exists in this hospital.");

    // === Intake rules (ServiceLevelCapacity) ===

    public static Error UnknownLevel(int levelId) => Error.NotFound(
        "Services.UnknownLevel",
        $"Le niveau {levelId} n'existe pas — impossible de lui accorder un quota.");

    public static Error DuplicateLevelQuota(int levelId) => Error.Conflict(
        "Services.DuplicateLevelQuota",
        $"Le niveau {levelId} apparaît deux fois dans les quotas : une promotion ne peut avoir qu'un seul quota par service.");

    // No "quota exceeds the service's capacity" rule: quotas replace Service.Capacity rather than
    // sitting under it, so on a restricted service that number governs nothing and a quota above it
    // contradicts nothing. See ServiceLevelCapacity.
}
