using PGSH.Application.Stages.ServicePeriods;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.MyServices;

/// <summary>
/// One slice of a chef's worklist, plus how big every other slice is.
///
/// <para>The slice is a <see cref="ServicePeriodState"/> — the domain's own four-way split of where a
/// rotation stands, not a vocabulary invented for this screen. That is what <em>bounds</em> the
/// list, and it is deliberately not the academic year: measured 2026-08-29, one chef's two services
/// held 3 220 periods reaching back to 2019, all returned unpaginated and mounted at once, while
/// year scoping had already blanked live worklists twice because an <c>AcademicYear</c> record
/// drifts out of step with the dates rotations really run on. A period's own lifecycle cannot drift
/// from itself.</para>
///
/// <para>The year is a second, narrower filter on top of that — useful, defaulted to the current
/// one, and made safe by <see cref="OutsideYearCount"/> rather than by being kept away from the live
/// slices. The two axes answer different questions: the state says whether a rotation is work, the
/// year says which campaign it belongs to — read off the registration, which is where the schema
/// records it.</para>
///
/// <para>The counts travel with the page deliberately. A bounded list has one failure mode the
/// unbounded one did not: the slice you land on is empty and the screen is indistinguishable from
/// "this chef has no work at all" — which is exactly the report that started this. Carrying every
/// slice's size means the UI can always say where the work is, and open on a slice that has some,
/// without fetching an extra row.</para>
/// </summary>
/// <param name="AcademicYearId">
/// The year actually applied — echoed back because it is usually one the handler resolved rather
/// than one the caller sent, and a selector that has to guess which year it is showing is a second
/// place for the answer to live. <c>null</c> means the read spans every year.
/// </param>
/// <param name="OutsideYearCount">
/// How many further periods of <paramref name="State"/> the year filter is holding back — 0 when no
/// year is applied. ⚠ This is the whole reason a year filter is safe on the live slices at all. Year
/// scoping blanked chef worklists twice, and both times the failure was silent: the screen showing
/// nothing looked exactly like a service with nothing to do. A slice narrowed to 0 that can say
/// « et 14 autres hors de cette année » cannot be misread, so the filter is now a question the chef
/// can answer rather than a trapdoor.
/// </param>
public sealed record ChefWorklistResponse(
    PaginatedResponse<ServicePeriodResponse> Page,
    ServicePeriodState State,
    ChefWorklistCounts Counts,
    int? AcademicYearId,
    int OutsideYearCount);

/// <summary>
/// How many periods sit in each state, over the same services, year and search as the page.
/// Computed from the very same predicates that select the rows
/// (<see cref="ServicePeriodLifecycle"/>), so a badge and its list cannot disagree.
/// </summary>
public sealed record ChefWorklistCounts(
    int Planned,
    int Underway,
    int AwaitingEvaluation,
    int Settled)
{
    /// <summary>Every period of the services in scope — the four states partition them.</summary>
    public int Total => Planned + Underway + AwaitingEvaluation + Settled;

    public int For(ServicePeriodState state) => state switch
    {
        ServicePeriodState.Planned            => Planned,
        ServicePeriodState.Underway           => Underway,
        ServicePeriodState.AwaitingEvaluation => AwaitingEvaluation,
        _                                     => Settled,
    };
}
