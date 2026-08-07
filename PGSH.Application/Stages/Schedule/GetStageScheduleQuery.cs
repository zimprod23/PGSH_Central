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

public sealed record SlotCellResponse(
    int    AssignmentId,
    int    StageSlotId,
    int    ServiceId,
    string ServiceName,
    string HospitalName,
    int    ServiceCapacity,
    int    OccupiedSeats);
