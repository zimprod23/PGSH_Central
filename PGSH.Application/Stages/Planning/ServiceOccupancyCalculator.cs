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
    /// <summary>
    /// The most students on <paramref name="serviceId"/> at any one moment inside [start, end],
    /// across all stages.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>The peak inside the window, never the sum of everything that touches it.</b> This
    /// summed, and two cells that each overlap the window <i>without overlapping each other</i> were
    /// added together. Measured on the live plan 2026-09-03: asked for Pharmacie Clinique 1's P2
    /// (06/10 → 03/11) on Pédiatrie2, it returned <b>118</b> — 56 from 4ᵉ année Pédiatrie P1
    /// (07/09 → <b>06/10</b>), 56 from its P2 (<b>07/10</b> → 06/11) and 6 from the pharmaciens. The
    /// two Pédiatrie columns are consecutive and never coexist; the service holds <b>62</b> on every
    /// single day of that window.</para>
    ///
    /// <para>It is the readable kind of wrong: the number is plausible, and it appears in three
    /// places at once — the planning grid's saturation, <c>RotationArranger</c>'s balance, and
    /// <c>SchedulePublisher</c>'s pre-publish guard. So publication was refused over loads that never
    /// occur. The per-service page and the charge report were right throughout, because they go
    /// through <c>OccupancyTimeline</c>, which cuts at boundaries — this now does the same
    /// arithmetic, so the four can no longer disagree.</para>
    ///
    /// <para>⚠ This makes the guard <b>less</b> strict, and correctly so. It never made it miss a real
    /// breach: a genuine simultaneous overload is still a moment at which the sum exceeds the
    /// ceiling, and that moment is one of the candidates evaluated below.</para>
    /// </remarks>
    public int LoadOn(int serviceId, DateOnly start, DateOnly end) =>
        PeakWithin(entries.Where(e => e.ServiceId == serviceId), start, end);

    /// <summary>
    /// The share of that peak belonging to one level — what a per-level quota is measured against.
    /// A service holding 10 first-years and 15 third-years is at 25 against its ceiling but at 10
    /// against the first-year quota.
    /// </summary>
    public int LoadOn(int serviceId, int levelId, DateOnly start, DateOnly end) =>
        PeakWithin(entries.Where(e => e.ServiceId == serviceId && e.LevelId == levelId), start, end);

    /// <summary>
    /// Sweeps the window and returns the highest simultaneous load in it.
    /// </summary>
    /// <remarks>
    /// The load is a step function that only ever rises when a window opens, so the maximum inside
    /// [start, end] is reached either on <paramref name="start"/> itself or on the first day of some
    /// entry that begins inside it. Evaluating those candidates is exact — no sampling, and no need
    /// to walk a day at a time.
    /// </remarks>
    private static int PeakWithin(IEnumerable<OccupancyEntry> scoped, DateOnly start, DateOnly end)
    {
        var overlapping = scoped.Where(e => e.Start <= end && start <= e.End).ToList();

        if (overlapping.Count == 0)
            return 0;

        var candidates = overlapping
            .Select(e => e.Start)
            .Where(day => day > start && day <= end)
            .Append(start)
            .Distinct();

        return candidates.Max(day =>
            overlapping.Where(e => e.Start <= day && day <= e.End).Sum(e => e.Students));
    }

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
