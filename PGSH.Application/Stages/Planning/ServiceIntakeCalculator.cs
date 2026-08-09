using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// In-memory view over a set of services' intake rules: whether each takes a given level, and how
/// many of them. It answers by delegating to <see cref="Service.Admits"/> / <see cref="Service.CapacityFor"/>
/// rather than re-deriving the rule, so "no rows means unrestricted" is stated once, in the domain.
///
/// Pairs with <see cref="ServiceOccupancyLookup"/>: that one says how many students are <i>there</i>,
/// this one says how many are <i>allowed</i>. Every capacity decision compares the two.
/// </summary>
internal sealed class ServiceIntakeLookup(IReadOnlyDictionary<int, Service> services)
{
    /// <summary>An unknown service admits nobody — a caller asking about one it never loaded is a bug, not a permit.</summary>
    public bool Admits(int serviceId, int levelId) =>
        services.TryGetValue(serviceId, out var service) && service.Admits(levelId);

    public int CapacityFor(int serviceId, int levelId) =>
        services.TryGetValue(serviceId, out var service) ? service.CapacityFor(levelId) : 0;

    public int TotalCapacity(int serviceId) =>
        services.TryGetValue(serviceId, out var service) ? service.Capacity : 0;

    public bool HasLevelRestrictions(int serviceId) =>
        services.TryGetValue(serviceId, out var service) && service.HasLevelRestrictions;

    public string NameOf(int serviceId) =>
        services.TryGetValue(serviceId, out var service) ? service.Name : $"#{serviceId}";
}

internal sealed class ServiceIntakeCalculator(IApplicationDbContext dbContext)
{
    public async Task<ServiceIntakeLookup> BuildAsync(
        IReadOnlyCollection<int> serviceIds, CancellationToken ct)
    {
        if (serviceIds.Count == 0)
            return new ServiceIntakeLookup(new Dictionary<int, Service>());

        var services = await dbContext.Services
            .AsNoTracking()
            .Include(s => s.LevelCapacities)
            .Where(s => serviceIds.Contains(s.Id))
            .ToListAsync(ct);

        return new ServiceIntakeLookup(services.ToDictionary(s => s.Id));
    }
}
