using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// One planned occupancy of a service: a cohort's students present in a service
/// over the window of one slot. Drawn from every stage, so the load it represents
/// is global — the same physical service used by different partitions/stages over
/// overlapping dates is summed, not counted per stage.
///
/// <paramref name="LevelId"/> is the level of the stage the cohort is doing, which is what a
/// service's intake quotas are written against. Two promotions sharing a service on overlapping
/// dates consume its total ceiling together but their own quotas separately.
/// </summary>
internal sealed record OccupancyEntry(
    int ServiceId, int LevelId, int CohortId, int StageSlotId, DateOnly Start, DateOnly End, int Students);

/// <summary>
/// In-memory view over the planned occupancy of a set of services. Two windows
/// overlap when <c>a.Start &lt;= b.End &amp;&amp; b.Start &lt;= a.End</c>; students whose
/// window overlaps the queried window are physically present at the same time and
/// therefore consume the same capacity.
/// </summary>
internal sealed class ServiceOccupancyLookup(IReadOnlyList<OccupancyEntry> entries)
{
    /// <summary>Total students on <paramref name="serviceId"/> whose slot window overlaps [start, end], across all stages.</summary>
    public int LoadOn(int serviceId, DateOnly start, DateOnly end) =>
        entries
            .Where(e => e.ServiceId == serviceId && e.Start <= end && start <= e.End)
            .Sum(e => e.Students);

    /// <summary>
    /// The share of that load belonging to one level — what a per-level quota is measured against.
    /// A service holding 10 first-years and 15 third-years is at 25 against its ceiling but at 10
    /// against the first-year quota.
    /// </summary>
    public int LoadOn(int serviceId, int levelId, DateOnly start, DateOnly end) =>
        entries
            .Where(e => e.ServiceId == serviceId && e.LevelId == levelId && e.Start <= end && start <= e.End)
            .Sum(e => e.Students);

    /// <summary>The levels actually present on a service over a window, for reporting which quota broke.</summary>
    public IReadOnlyList<int> LevelsOn(int serviceId, DateOnly start, DateOnly end) =>
        entries
            .Where(e => e.ServiceId == serviceId && e.Start <= end && start <= e.End)
            .Select(e => e.LevelId)
            .Distinct()
            .ToList();
}

/// <summary>
/// Builds a <see cref="ServiceOccupancyLookup"/> for a set of services by reading
/// every planned slot assignment that targets them — regardless of stage. This is
/// the single source for cross-stage capacity: display occupancy, auto-arrange
/// saturation, and the pre-publish guard all measure load the same way.
/// </summary>
internal sealed class ServiceOccupancyCalculator(IApplicationDbContext dbContext)
{
    public async Task<ServiceOccupancyLookup> BuildAsync(
        IReadOnlyCollection<int> serviceIds, CancellationToken ct)
    {
        if (serviceIds.Count == 0)
            return new ServiceOccupancyLookup([]);

        var entries = await EntriesQuery(dbContext, serviceIds).ToListAsync(ct);

        return new ServiceOccupancyLookup(entries);
    }

    /// <summary>
    /// Every planned cell targeting these services, whatever stage or year it belongs to — the load
    /// half of every capacity decision.
    /// </summary>
    /// <remarks>
    /// ⚠ Named so <c>SqlTranslationTests</c> can compile it: the projection aggregates a navigation
    /// collection (<c>a.Cohort.Assignments.Count</c>) across three hops, and a projection over a
    /// navigation collection is the shape that took the macro plan down on 2026-08-26.
    /// </remarks>
    internal static IQueryable<OccupancyEntry> EntriesQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> serviceIds) =>
        dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => serviceIds.Contains(a.ServiceId))
            .Select(a => new OccupancyEntry(
                a.ServiceId,
                a.Cohort.Stage.LevelId,
                a.CohortId,
                a.StageSlotId,
                a.StageSlot.StartDate,
                a.StageSlot.EndDate,
                a.Cohort.Assignments.Count));
}
