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

    /// <summary>
    /// Whether « autoriser le dépassement d'effectif » may lift this service's number — the chef's
    /// own statement, read off <see cref="Service.AllowsOverCapacity"/> rather than restated here.
    /// </summary>
    /// <remarks>
    /// ⚠ An unknown service refuses, for the same reason it admits nobody: a caller asking about one
    /// it never loaded is a bug, and the safe answer to a bug is the one that stops a publication
    /// rather than the one that forces it through.
    /// </remarks>
    public bool AllowsOverCapacity(int serviceId) =>
        services.TryGetValue(serviceId, out var service) && service.AllowsOverCapacity;

    /// <summary>The services among <paramref name="serviceIds"/> whose number cannot be forced.</summary>
    public IReadOnlyList<int> FirmServicesAmong(IEnumerable<int> serviceIds) =>
        serviceIds.Where(id => !AllowsOverCapacity(id)).ToList();

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
