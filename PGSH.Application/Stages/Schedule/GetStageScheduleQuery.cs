using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Schedule;

/// <summary>
/// The planning grid of one stage for one year: the columns of the axis, a page of cohort rows, and
/// the aggregate the screen needs to describe what it is not showing.
/// </summary>
/// <remarks>
/// ⚠ <b>The rows are paged, and they have to be.</b> The response is a single object, which is
/// exactly the shape that hides an unbounded collection from any <c>List&lt;T&gt;</c> grep — the same
/// trap as <c>GetGroupByIdQuery</c> and its 4 725 students. Measured 2026-08-31, the current year's
/// biggest stage carries <b>105 cohortes over ten columns</b>: a thousand cells in one payload, and a
/// thousand cell components mounted at once in the browser, which is what made the grid take seconds
/// to open <i>and</i> seconds to close — closing does no server work at all, so the cost was never on
/// this side.
/// <para><paramref name="RotationGroup"/> narrows to one partition <b>server-side</b>. Filtering the
/// rows the client happens to hold answers « aucune cohorte » for anyone sitting on page 3, and
/// nothing distinguishes that from a partition nobody has cut — the same reason the chef worklist's
/// search had to move to the server with its pagination.</para>
/// </remarks>
public sealed record GetStageScheduleQuery(
    int StageId,
    int? AcademicYearId = null,
    string? RotationGroup = null,
    int PageNumber = 1,
    int PageSize = GetStageScheduleQuery.DefaultPageSize) : IQuery<StageScheduleResponse>
{
    /// <summary>
    /// Cohortes per page. Ten columns of a dozen elements each is enough to fill any screen and small
    /// enough that mounting and unmounting the grid is instant.
    /// </summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    /// ⚠ A non-positive page size means "unstated", never "one row". <c>ToPaginatedResponseAsync</c>
    /// clamps a 0 <em>upward</em> to 1, so <c>?pageSize=0</c> — or any binding that fails to a zero —
    /// would answer a promotion with a single cohorte and nothing anywhere saying so. Same reasoning
    /// for the page number.
    /// </summary>
    public int EffectivePageNumber => PageNumber > 0 ? PageNumber : 1;

    public int EffectivePageSize => PageSize > 0 ? PageSize : DefaultPageSize;
}

public sealed record StageScheduleResponse(
    int StageId,
    IReadOnlyList<StageSlotResponse> Slots,
    PaginatedResponse<CohortScheduleRow> Cohorts,
    StageScheduleSummary Summary);

/// <summary>
/// What is true of the whole selection, whichever page is on screen.
/// </summary>
/// <remarks>
/// ⚠ <b>A bounded list has a failure mode the unbounded one did not.</b> Counting the rows the client
/// holds was correct only while it held all of them; left as it was, a page of 25 would have reported
/// « 3 configurées » on a stage with 90, and the publish button beside it would have promised to
/// publish 3. Every number here is measured where the rows are — the rule this codebase already
/// applies to badges: to show a count, ask the server for it.
/// <para><paramref name="Partitions"/> is deliberately <b>not</b> narrowed by
/// <c>RotationGroup</c>: they are the chips the user filters <i>with</i>, so filtering them by the
/// current filter would leave no way back.</para>
/// </remarks>
public sealed record StageScheduleSummary(
    int TotalCohorts,
    int PublishedCohorts,
    int ConfiguredUnpublishedCohorts,
    IReadOnlyList<PartitionSummary> Partitions,
    int SaturatedCellCount,
    IReadOnlyList<SaturatedCellResponse> Saturations,
    IReadOnlyList<int> OccupiedSlotIds,
    IReadOnlyList<PartitionSlotUse> PartitionUsage);

/// <summary>One rotation partition of the promotion, and how many of this stage's cohorts carry it.</summary>
public sealed record PartitionSummary(string Label, int CohortCount);

/// <summary>
/// One partition standing in one column of this stage — the whole stage, never the current filter.
/// </summary>
/// <remarks>
/// ⚠ It is what tells « répartir la partition A sur P4-P6 » that B is already in those columns, and
/// that question cannot be answered from the rows on screen: filtering to A removes exactly the
/// cells the warning is about. Bounded by partitions × columns whatever the promotion's size.
/// </remarks>
public sealed record PartitionSlotUse(string? RotationGroup, int StageSlotId);

/// <summary>
/// Why a (créneau × service) will refuse the publish. Named rather than inferred from the numbers,
/// because the three are fixed in different places: move groups, raise the promotion's quota, or
/// raise the service's own capacity.
/// </summary>
public enum SaturationReason
{
    /// <summary>Over the service's total, counted across every promotion sharing it.</summary>
    Total,

    /// <summary>Over the quota this service grants the stage's promotion.</summary>
    Quota,

    /// <summary>The service carries intake rules and none names this promotion — not forceable.</summary>
    Refused,
}

/// <summary>
/// One (créneau × service) the publish would refuse. Deduplicated: it is a fact about the pair, not
/// about each cohorte standing in it, and a dozen cohortes in one saturated service is one problem.
/// </summary>
public sealed record SaturatedCellResponse(
    int    StageSlotId,
    int    PeriodNumber,
    int    ServiceId,
    string ServiceName,
    string HospitalName,
    int    OccupiedSeats,
    int    Capacity,
    SaturationReason Reason);

public sealed record StageSlotResponse(
    int      Id,
    int      PeriodNumber,
    string?  Label,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record CohortScheduleRow(
    int     CohortId,
    string  CohortLabel,
    int     AcademicGroupId,
    string  AcademicGroupLabel,
    string? RotationGroup,
    int     StudentCount,
    bool    IsSchedulePublished,
    IReadOnlyList<SlotCellResponse?> Cells);

/// <summary>
/// <paramref name="Capacity"/> and <paramref name="OccupiedSeats"/> are the <b>one</b> limit that
/// actually governs this cell and the load measured against it — never two competing numbers, because
/// quotas replace a service's total rather than sitting under it.
/// <paramref name="IsLevelQuota"/> says which rule is in force, and therefore what the numbers count:
/// <list type="bullet">
///   <item><b>true</b> — the quota this service grants the stage's promotion, against that
///   promotion's students alone. The service's own total is not consulted.</item>
///   <item><b>false</b> — the service's total, against every promotion sharing it over these dates.</item>
/// </list>
/// <paramref name="AdmitsLevel"/> is false for a cell someone placed on a service that refuses this
/// promotion outright — publish will reject it, so the grid must say so before they try.
/// </summary>
public sealed record SlotCellResponse(
    int    AssignmentId,
    int    StageSlotId,
    int    ServiceId,
    string ServiceName,
    string HospitalName,
    int    Capacity,
    int    OccupiedSeats,
    bool   IsLevelQuota,
    bool   AdmitsLevel);
