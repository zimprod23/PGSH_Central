using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Hospitals.Services.OccupancyReport;

namespace PGSH.Application.Hospitals.Services.PromotionFit;

/// <summary>
/// « Cette promotion tient-elle ? » — what each stage of a promotion <b>needs</b> against what its
/// authorised services <b>hold</b>, read before anything is planned.
///
/// <para><b>Why it exists, and why it is not <see cref="GetOccupancyReportQuery"/>.</b> The report
/// measures pressure <i>after</i> placement: it reads the cells, so on a promotion that has not been
/// cut yet it prints <b>zero</b> — comfortably empty — until the whole day of cutting, laying the
/// axis and arranging is done and somebody is standing at the « Publier » button. That is the moment
/// the shortfall is discovered today, and it is the most expensive moment available. This read needs
/// no plan at all: headcount, durations and the capacities of the authorised services are the whole
/// input.</para>
///
/// <para>⚠ <b>It warns; it does not place better.</b> <c>RotationArranger.BuildServiceQueue</c>
/// weights by capacity and never reads live occupancy — deliberately, so a service cannot be
/// reserved by filling it first — so arranging the 4ᵉ MED spreads it over the services the 3ᵉ MED is
/// already sitting in as though they were empty. The sequel is unchanged: read the number, correct
/// the catalogue, then arrange. What this changes is <i>when</i> the faculty's decision gets
/// taken.</para>
///
/// <para>⚠ <b>A filter narrows what is listed, never what is counted.</b> Services are shared
/// between promotions, so the « aussi autorisé par » column is computed over every promotion of the
/// year even when one promotion is asked about — same rule as the occupancy report, for the same
/// reason: hiding half of a service's claimants makes it read roomy exactly where it is not.</para>
/// </summary>
/// <param name="AcademicYearId">Omitted resolves to the current year, never to all of them.</param>
/// <param name="LevelId">
/// One promotion instead of all of them. ⚠ « Retrait » (<c>Level.Year == 0</c>) is not a promotion
/// and is refused rather than reported empty — nothing is ever planned for a marker.
/// </param>
public sealed record GetPromotionFitQuery(
    int? AcademicYearId = null,
    int? LevelId = null) : IQuery<PromotionFitResponse>;
