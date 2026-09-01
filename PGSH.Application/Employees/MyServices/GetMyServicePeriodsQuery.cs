using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;

namespace PGSH.Application.Employees.MyServices;

/// <summary>
/// Service periods scoped to the services the current employee is chef of. The chef's
/// services are derived server-side from the identity — a <paramref name="ServiceId"/>
/// the caller does not lead is silently ignored, so a chef can never read another
/// chef's worklist.
/// </summary>
/// <param name="State">
/// Which slice of the service's rotations to return; <see cref="ServicePeriodState.Underway"/> when
/// omitted — never <see cref="ServicePeriodState.Settled"/>, the one slice that grows without bound.
/// <para>Nullable, and resolved through <see cref="EffectiveState"/>, so that "absent" is one
/// concrete value the handler chooses rather than <c>default(ServicePeriodState)</c> — which is
/// <see cref="ServicePeriodState.Planned"/>, the one slice a chef can act on nothing in.
/// <c>[AsParameters]</c> does honour a declared default (measured on .NET 9, and pinned by
/// <c>ChefWorklistEndpointTests</c>); the nullable form makes the fallback independent of that, and
/// of the order the enum happens to be written in.</para>
/// </param>
/// <param name="AcademicYearId">
/// The year to scope to; the year flagged current when omitted, per the rule that an absent year
/// means the current one and never all of them.
/// <para>Matched against the year a period's <b>registration</b> names — a fact the schema already
/// carries, through two NOT NULL foreign keys — never against the year's calendar span. The dates
/// only ever approximated it, and disagreed with it on 6.7% of the base.</para>
/// <para>⚠ Year scoping is the change that blanked chef worklists twice, so it is never the whole
/// answer here: <paramref name="State"/> is what bounds the list, and
/// <c>ChefWorklistResponse.OutsideYearCount</c> says how much of the slice this filter is holding
/// back, so a narrowed list can never be mistaken for an empty one.</para>
/// </param>
/// <param name="AllYears">
/// The explicit way to span every year — the "some other way" an intentionally unscoped read has to
/// say so. It wins over <paramref name="AcademicYearId"/>, because the two together can only come
/// from a caller that has already changed its mind.
/// </param>
/// <param name="SearchTerm">
/// Name, CNE or Apogée. ⚠ Applied server-side, and it has to be: the list is a page now, so a client
/// filtering the rows it holds answers "no such student in this service" for anyone who happens to
/// be on another page. It narrows the counts as well as the rows, which turns the badges into an
/// answer to "where is this student?" across the four slices.
/// </param>
public sealed record GetMyServicePeriodsQuery(
    int? ServiceId = null,
    ServicePeriodState? State = null,
    int? AcademicYearId = null,
    bool AllYears = false,
    string? SearchTerm = null,
    int? PageNumber = null,
    int? PageSize = null) : IQuery<ChefWorklistResponse>
{
    /// <summary>Rows per page when the caller does not say. Equal to <c>MaxPageSize</c>, so a slice
    /// that fits in one page — which every live slice normally does — arrives whole.</summary>
    public const int DefaultPageSize = 200;

    public ServicePeriodState EffectiveState => State ?? ServicePeriodState.Underway;

    /// <summary>
    /// ⚠ A non-positive page size means "unstated", never "one row". <c>ToPaginatedResponseAsync</c>
    /// clamps a 0 <em>upward</em> to 1, so <c>?pageSize=0</c> — or any binding that fails to a zero —
    /// would answer a fifty-student window with one student and nothing anywhere saying so. Same
    /// reasoning for the page number.
    /// </summary>
    public int EffectivePageNumber => PageNumber is > 0 ? PageNumber.Value : 1;

    public int EffectivePageSize => PageSize is > 0 ? PageSize.Value : DefaultPageSize;
}
