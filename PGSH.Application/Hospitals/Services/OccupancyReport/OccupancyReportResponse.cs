using PGSH.Application.Hospitals.Services.Occupancy;

namespace PGSH.Application.Hospitals.Services.OccupancyReport;

/// <summary>
/// One year of placement pressure across every service in scope.
///
/// <para>⚠ <b>What the filters do and do not narrow.</b> A filter picks which services are
/// <em>listed</em> and which placements are attributed to <see cref="OccupancyServiceRow.Share"/>;
/// it never narrows the load a saturation verdict is measured on. A service is shared, and the
/// ceiling that refuses a publish counts every promotion standing in it — so a report that measured
/// « la 5ᵉ année seule » against the service total would print « ok » for a service that is over
/// because of the 3ᵉ. That is the same class of mistake as reading an omitted year as « toutes »:
/// one number quietly standing in for another.</para>
/// </summary>
public sealed record OccupancyReportResponse(
    int AcademicYearId,
    string AcademicYearLabel,
    DateOnly YearStart,
    DateOnly YearEnd,

    /// <summary>A sentence naming the filters, so a printed copy states its own scope.</summary>
    string Scope,

    OccupancyReportTotals Totals,

    /// <summary>
    /// The faculty's simultaneous load folded into months — the <b>maximum</b> reached inside each
    /// month, read off the exact segments rather than sampled, so it is the real peak of that month
    /// and not an average that hides it.
    /// </summary>
    IReadOnlyList<OccupancyMonthBar> Months,

    IReadOnlyList<OccupancyServiceRow> Services,
    IReadOnlyList<OccupancyStageRow> Stages,
    IReadOnlyList<OccupancyLevelRow> Levels,

    /// <summary>
    /// What the report looked for and did not find. Same rule as <c>ExportNotes</c>: silent when the
    /// data has nothing to say, because a warning that fires whatever the numbers are is noise, and
    /// noise is dismissed — which puts the real one out of sight.
    /// </summary>
    IReadOnlyList<string> Notes);

public sealed record OccupancyReportTotals(
    int ServicesInScope,
    int ServicesOccupied,

    /// <summary>
    /// ⚠ In scope and holding nobody all year. Invisible from a service's own page, where it looks
    /// like an ordinary quiet service — and it is half of the balance defect: the arrangement put
    /// everyone somewhere else.
    /// </summary>
    int ServicesNeverUsed,

    int ServicesOverCapacity,

    /// <summary>Services holding a promotion their own quotas do not admit — the un-waivable fault.</summary>
    int ServicesAdmittingNobody,

    /// <summary>Grid cells in scope. 0 means nothing has been arranged, which is not saturation.</summary>
    int PlacementCount,

    /// <summary>Sum of the students placed, counting a cohort once per cell it occupies.</summary>
    int PlacedStudents,

    int DistinctStages,
    int DistinctLevels,

    /// <summary>The faculty's highest simultaneous load.</summary>
    int PeakStudents,

    /// <summary>
    /// ⚠ The <b>envelope</b> of the peak — the first day it is reached and the last — not the first
    /// segment that happens to hit it. A load held from September to March is reached on dozens of
    /// consecutive segments, and reporting the first one's window announced « du 07/09 au 06/10 » for
    /// a plateau six months long. The chart beside it said otherwise, which is how it was caught.
    /// </summary>
    DateOnly? PeakStart,
    DateOnly? PeakEnd,

    /// <summary>
    /// Days actually spent at that load. Says whether the peak is a plateau or one bad fortnight —
    /// which the envelope alone cannot, since the segments at the peak need not be contiguous.
    /// </summary>
    int PeakDays,

    /// <summary>
    /// ⚠ <b>Jours-service</b>, not days: one service over its limit for ten days and ten services
    /// over for one day both read 10. Summing across services is the right measure of total
    /// pressure, but it must never be labelled « jours » — same trap as counting placements and
    /// calling them students.
    /// </summary>
    int ServiceDaysOverCapacity);

public sealed record OccupancyMonthBar(
    int Year,
    int Month,
    string Label,
    int PeakStudents,
    int ServicesOccupied,
    int ServicesOverCapacity,

    /// <summary>
    /// How the month's peak splits between promotions, read off <b>that same segment</b> — so the
    /// parts add up to <see cref="PeakStudents"/> exactly and the bar can be stacked. Taking each
    /// promotion's own peak instead would sum to more than the total, because two promotions do not
    /// peak on the same day.
    /// </summary>
    IReadOnlyList<MonthLevelLoad> Levels);

public sealed record MonthLevelLoad(int LevelId, string LevelLabel, int Students);

/// <summary>One service's year, condensed. The occupant detail stays on the service's own page.</summary>
public sealed record OccupancyServiceRow(
    int ServiceId,
    string ServiceName,
    int HospitalId,
    string HospitalName,
    string HospitalCity,

    CapacityRule Rule,

    /// <summary>
    /// The limit the saturation is measured against: <c>Service.Capacity</c> when unrestricted,
    /// otherwise the sum of the quotas of the promotions actually present — the only honest
    /// denominator when the ceiling is per promotion. 0 when a present promotion is not admitted.
    /// </summary>
    int Ceiling,
    int TotalCapacity,
    IReadOnlyList<LevelQuotaResponse> Quotas,

    int SegmentCount,
    int PeakStudents,
    DateOnly? PeakStart,
    DateOnly? PeakEnd,

    /// <summary>Peak ÷ ceiling. Null when there is no ceiling to divide by — never 0, which sorts
    /// as « the least saturated » and is exactly wrong for a service admitting nobody.</summary>
    decimal? Saturation,

    int OverCapacitySegments,
    int DaysOverCapacity,

    /// <summary>Promotions present that the service's quotas do not name.</summary>
    IReadOnlyList<string> LevelsNotAdmitted,

    /// <summary>
    /// Students attributed to this service under the report's filters, out of its whole load.
    /// Equal to the load when nothing is filtered.
    /// </summary>
    int Share,

    IReadOnlyList<OccupancyBand> Bands,
    IReadOnlyList<OccupancyServiceLevel> Levels,
    IReadOnlyList<OccupancyServiceStage> Stages);

/// <summary>A stretch over which the service's occupants do not change — the unit the chart draws.</summary>
public sealed record OccupancyBand(
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    int Students,
    int? Capacity,
    int Overflow);

public sealed record OccupancyServiceLevel(int LevelId, string LevelLabel, int PeakStudents, int? Capacity, bool NotAdmitted);

public sealed record OccupancyServiceStage(int StageId, string StageName, string LevelLabel, int Cells, int Students);

/// <summary>
/// One stage's use of the services it is allowed to send students to.
///
/// <para>⚠ <see cref="ServicesUnused"/> is the number this whole report exists for. A stage that
/// lists five services and places everybody in two has an arrangement defect no single service page
/// can show — the two empty ones look like services with nothing planned.</para>
/// </summary>
public sealed record OccupancyStageRow(
    int StageId,
    string StageName,
    int LevelId,
    string LevelLabel,
    int ServicesAllowed,
    int ServicesUsed,
    int ServicesUnused,
    int Cells,
    int PlacedStudents,

    /// <summary>Highest number of students this stage puts in a single service at one time.</summary>
    int HeaviestServiceLoad,
    string? HeaviestServiceName);

public sealed record OccupancyLevelRow(
    int LevelId,
    string LevelLabel,
    int ServicesUsed,
    int Cells,
    int PlacedStudents,
    int PeakStudents,

    /// <summary>Services this promotion stands in that do not admit it.</summary>
    int ServicesNotAdmitting);
