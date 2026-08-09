using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Schedule;

public sealed record GetStageScheduleQuery(int StageId, int? AcademicYearId = null) : IQuery<StageScheduleResponse>;

public sealed record StageScheduleResponse(
    int StageId,
    IReadOnlyList<StageSlotResponse> Slots,
    IReadOnlyList<CohortScheduleRow> Cohorts);

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
