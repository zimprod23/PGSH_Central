using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Hospitals.Services.Occupancy;

namespace PGSH.Application.Hospitals.Services.OccupancyReport;

/// <summary>
/// The whole faculty's placement pressure for one year, service by service — the cross-service half
/// of <see cref="GetServiceOccupancyQuery"/>.
///
/// <para><b>Why it exists.</b> Capacity is felt in a service and was only ever readable one service
/// at a time: the service page answers « what does <em>this</em> service hold », and nothing answered
/// « which services are the problem ». That question is the one somebody asks before publishing a
/// promotion, and answering it by opening 148 pages is not answering it.</para>
///
/// <para>⚠ <b>Two facts only a cross-service read can state.</b> A service that is never used all
/// year is invisible from its own page — it looks like a service with nothing planned, which is what
/// it is, and says nothing about the stage that had five services and used two of them. Measured on
/// 5MED Psychiatrie in a previous session: <b>all nine columns went to a single service and two of
/// the five were never used</b>, and the printed répartition was the only place it showed. The same
/// goes for a promotion the quotas do not admit: the refusal that names it is un-waivable, and it is
/// cheaper to read before the publish than during it.</para>
///
/// <para>The year is resolved the usual way — omitted means the current one, never all of them — and
/// bounded by the year's <i>dates</i> rather than by <c>AcademicYearId</c>, exactly as the
/// per-service read is, so the two cannot disagree about the same service.</para>
/// </summary>
/// <param name="AcademicYearId">Omitted resolves to the current year.</param>
/// <param name="HospitalId">Narrows to one hospital's services. The report still counts a service's
/// whole load: a service is shared, and hiding half of its occupants would make its saturation read
/// low for the reason the reader is looking at it.</param>
/// <param name="LevelId">Narrows the <b>placements</b> to one promotion, so « où est-ce que la 5ᵉ
/// année pousse » is answerable. ⚠ The ceilings do not narrow with it — see the handler.</param>
/// <param name="StageId">Narrows the placements to one stage, same reading as <paramref name="LevelId"/>.</param>
/// <param name="OnlySaturated">Keep only services that go over a limit at some point in the year.</param>
public sealed record GetOccupancyReportQuery(
    int? AcademicYearId = null,
    int? HospitalId = null,
    int? LevelId = null,
    int? StageId = null,
    bool OnlySaturated = false) : IQuery<OccupancyReportResponse>;
