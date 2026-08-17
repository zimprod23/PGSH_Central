using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.Services.Occupancy;

/// <summary>
/// What a service actually holds, day by day, across every stage and promotion at once.
///
/// <para>The year is resolved the usual way — omitted means the current one, never all of them.
/// Unlike most year-scoped reads this one is bounded by the year's <i>dates</i> rather than by
/// <c>AcademicYearId</c>: the question is physical ("who is standing in this service in March"), and
/// a cell whose slot is stamped with the wrong year but dated inside this one is exactly the drift
/// the page exists to surface. Bounding by dates also makes this agree with
/// <c>SchedulePublisher</c>'s capacity guard by construction, which reads the same cells with no
/// year filter at all — a page that disagreed with the refusal it is meant to explain would be worse
/// than no page.</para>
/// </summary>
public sealed record GetServiceOccupancyQuery(int ServiceId, int? AcademicYearId)
    : IQuery<ServiceOccupancyResponse>;

/// <summary>How a service states its limit — see <c>Service.CapacityFor</c>.</summary>
public enum CapacityRule
{
    /// <summary>No quota authored: <c>Service.Capacity</c>, counted across every promotion at once.</summary>
    Total,

    /// <summary>
    /// Quotas authored: each promotion measured against its own, and <c>Service.Capacity</c> is not
    /// consulted at all. A promotion with no row is not admitted.
    /// </summary>
    PerLevel,
}

public sealed record ServiceOccupancyResponse(
    int ServiceId,
    string ServiceName,
    string HospitalName,
    int AcademicYearId,
    string AcademicYearLabel,
    CapacityRule Rule,
    /// <summary>
    /// <c>Service.Capacity</c>. Still sent when <see cref="Rule"/> is <see cref="CapacityRule.PerLevel"/>,
    /// because the form still shows it — but it is dead data there and the UI must say so.
    /// </summary>
    int TotalCapacity,
    IReadOnlyList<LevelQuotaResponse> Quotas,
    IReadOnlyList<OccupancySegmentResponse> Segments,
    OccupancySummaryResponse Summary);

public sealed record LevelQuotaResponse(int LevelId, string LevelLabel, int Capacity);

public sealed record OccupancySegmentResponse(
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    int Students,
    /// <summary>The ceiling in force over this stretch, or null when the service is restricted —
    /// there is no single number then, only one per promotion in <see cref="Levels"/>.</summary>
    int? Capacity,
    /// <summary>How many students over the limit, summed over whichever limits are in force. 0 when within.</summary>
    int Overflow,
    IReadOnlyList<SegmentLevelLoadResponse> Levels,
    IReadOnlyList<SegmentOccupantResponse> Occupants);

/// <summary>
/// One promotion's share of a segment. Sent even on an unrestricted service, where it carries no
/// ceiling of its own — knowing the 62 students are 30 third-years and 32 fifth-years is the whole
/// point of a service-level view, and it is what a quota would be authored against.
/// </summary>
public sealed record SegmentLevelLoadResponse(
    int LevelId,
    string LevelLabel,
    int Students,
    int? Capacity,
    int Overflow,
    /// <summary>The service has quotas and none of them names this promotion — it is not admitted at
    /// all, which is a different fault from being over a quota and reads differently.</summary>
    bool NotAdmitted);

public sealed record SegmentOccupantResponse(
    int StageId,
    string StageName,
    int LevelId,
    string LevelLabel,
    int PeriodNumber,
    /// <summary>Collapsed the way the répartition prints them: "47-50", "47-48, 50".</summary>
    string GroupNumbers,
    int CohortCount,
    int Students);

public sealed record OccupancySummaryResponse(
    int SegmentCount,
    int OverCapacitySegments,
    int PeakStudents,
    DateOnly? PeakStart,
    DateOnly? PeakEnd,
    int DistinctStages,
    int DistinctLevels,
    /// <summary>Days spent over the limit. The number that says whether a breach is a fortnight or a year.</summary>
    int DaysOverCapacity);
