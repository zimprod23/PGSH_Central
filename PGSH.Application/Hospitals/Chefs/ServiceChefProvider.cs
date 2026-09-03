using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;

namespace PGSH.Application.Hospitals.Chefs;

/// <summary>
/// Reads the three chef sources for a set of services and hands back a
/// <see cref="ServiceChefDirectory"/> — the database side of "who leads this service", in one place,
/// so no document assembles its own and gets a different name.
///
/// <para>The <see cref="ServiceChefSourcePolicy"/> is asked for by the caller and never assumed
/// here: a default would let a document narrow its sources without saying so on the page that
/// prints the name. Both callers pass <see cref="ServiceChefPolicy.InForce"/>.</para>
/// </summary>
public sealed class ServiceChefProvider(IApplicationDbContext dbContext)
{
    /// <summary>
    /// ⚠ <b>The whole tenure trail is loaded, not the one open on a date.</b> The répartition used to
    /// filter it in SQL because it asks a single as-of question; a document covering a year of
    /// rotations asks a different one per période, and a predicate cannot be pushed down for a date
    /// that is not known yet. The trail is bounded by the services in scope — 148 in the whole base,
    /// two of which carry a tenure at all — so the read is cheaper than the round trip it replaces.
    /// </summary>
    public async Task<ServiceChefDirectory> BuildAsync(
        IReadOnlyCollection<int> serviceIds,
        ServiceChefSourcePolicy policy,
        CancellationToken cancellationToken)
    {
        if (serviceIds.Count == 0)
            return ServiceChefDirectory.Empty;

        var services = await ServicesQuery(dbContext, serviceIds).ToListAsync(cancellationToken);

        // ⚠ Loaded under every policy, including the one that will not print them. The policy
        // narrows what a document may *name*, not what the directory *knows*:
        // ServiceChefDirectory.HasWithheldLinkedChef has to be able to say « quelqu'un est rattaché,
        // et ce n'est pas ce nom-là », and skipping the read makes that answer silently false —
        // which is the exact confusion the policy exists to remove.
        var tenures = await TenuresQuery(dbContext, serviceIds).ToListAsync(cancellationToken);

        var byService = tenures
            .GroupBy(t => t.ServiceId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ServiceChefTenure>)g
                    .Select(t => new ServiceChefTenure(t.Name, t.Start, t.End)).ToList());

        return new ServiceChefDirectory(
            services
                .Select(s => new ServiceChefRecord(
                    s.ServiceId,
                    s.SittingChefName,
                    s.Description,
                    byService.GetValueOrDefault(s.ServiceId, [])))
                .ToList(),
            policy);
    }

    /// <summary>
    /// ⚠ <b>Two flat queries, never one projection carrying the tenures.</b> A tenure projects to a
    /// computed element with no key of its own, and a collection of those folded inside a
    /// <c>Select</c> is the shape Npgsql refuses — the family that killed the macro plan with the
    /// whole suite green. Keyed on the parent id and folded in memory instead; both are named so
    /// <c>SqlTranslationTests</c> can compile them.
    /// </summary>
    internal static IQueryable<ServiceChefSourceRow> ServicesQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> serviceIds) =>
        dbContext.Services
            .AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .Select(s => new ServiceChefSourceRow(
                s.Id,
                s.ServiceChef == null ? null : s.ServiceChef.FirstName + " " + s.ServiceChef.LastName,
                // Parsed in memory, not in SQL: the legacy note is free text behind a known prefix,
                // and there are a few hundred services at most.
                s.Description));

    internal static IQueryable<ServiceChefTenureRow> TenuresQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> serviceIds) =>
        dbContext.Services
            .AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .SelectMany(s => s.ChefHistory)
            .OrderBy(h => h.StartDate)
            .Select(h => new ServiceChefTenureRow(
                h.ServiceId,
                h.Employee.FirstName + " " + h.Employee.LastName,
                h.StartDate,
                h.EndDate));
}

internal sealed record ServiceChefSourceRow(int ServiceId, string? SittingChefName, string? Description);

internal sealed record ServiceChefTenureRow(int ServiceId, string? Name, DateOnly Start, DateOnly? End);
