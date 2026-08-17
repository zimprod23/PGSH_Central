namespace PGSH.Application.Hospitals.Services.Occupancy;

/// <summary>One cohort's presence in a service over one period's window.</summary>
public sealed record OccupancyPlacement(
    int StageId,
    string StageName,
    int LevelId,
    string LevelLabel,
    int PeriodNumber,
    int CohortId,
    int GroupNumber,
    int Students,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>A stretch of days over which exactly the same people are in the service.</summary>
public sealed record OccupancySegment(
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<OccupancyPlacement> Occupants);

/// <summary>
/// Cuts a service's year into the stretches over which its occupants do not change.
///
/// <para>⚠ <b>A service's load is not readable one period at a time.</b> Nothing ties two stages'
/// periods together — <c>StageSlot</c> is keyed (stage, year, number), so Chirurgie P1 and ANES REA
/// P1 have independent dates and legitimately different lengths. List one row per slot and the
/// number you print for each is that slot's own cohorts, while the students actually standing in the
/// service on a given morning are the union of every window covering that day. The peak therefore
/// lives in the *overlap*, which a per-slot list never shows.</para>
///
/// <para>So the year is cut at every boundary — each window's first day, and the day after its last
/// — and each resulting segment carries one exact simultaneous load. Empty stretches are dropped:
/// a service with nobody in it in December has no December row, rather than a row reading zero.</para>
///
/// <para>Pure by design, like <c>PeriodAxis</c> and <c>RotationTiling</c>: no DB, no clock, so the
/// boundary arithmetic can be tested exhaustively instead of through a seeded database.</para>
/// </summary>
public static class OccupancyTimeline
{
    public static List<OccupancySegment> Build(IEnumerable<OccupancyPlacement> placements)
    {
        var all = placements.ToList();
        if (all.Count == 0)
            return [];

        // Half-open boundaries: a window [start, end] contributes `start` and `end + 1`, so the day
        // after a window closes opens a new segment. Using `end` itself would make the last day of
        // one window and the first of the next share a boundary and silently merge them.
        var boundaries = all
            .SelectMany(p => new[] { p.StartDate, p.EndDate.AddDays(1) })
            .Distinct()
            .Order()
            .ToList();

        var segments = new List<OccupancySegment>(boundaries.Count);

        for (int i = 0; i + 1 < boundaries.Count; i++)
        {
            var start = boundaries[i];
            var end = boundaries[i + 1].AddDays(-1);

            var occupants = all
                .Where(p => p.StartDate <= end && start <= p.EndDate)
                .OrderBy(p => p.LevelId)
                .ThenBy(p => p.StageName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.GroupNumber)
                .ToList();

            if (occupants.Count > 0)
                segments.Add(new OccupancySegment(start, end, occupants));
        }

        return segments;
    }
}
