using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// One planned occupancy of a service: a cohort's students present in a service
/// over the window of one slot. Drawn from every stage, so the load it represents
/// is global — the same physical service used by different partitions/stages over
/// overlapping dates is summed, not counted per stage.
/// </summary>
internal sealed record OccupancyEntry(int ServiceId, int CohortId, int StageSlotId, DateOnly Start, DateOnly End, int Students);

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

        var entries = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => serviceIds.Contains(a.ServiceId))
            .Select(a => new OccupancyEntry(
                a.ServiceId,
                a.CohortId,
                a.StageSlotId,
                a.StageSlot.StartDate,
                a.StageSlot.EndDate,
                a.Cohort.Assignments.Count))
            .ToListAsync(ct);

        return new ServiceOccupancyLookup(entries);
    }
}
