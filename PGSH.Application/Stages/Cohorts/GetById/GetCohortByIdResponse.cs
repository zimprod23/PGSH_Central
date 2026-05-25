namespace PGSH.Application.Stages.Cohorts.GetById;

public sealed record CohortResponse(
    int     Id,
    int     StageId,
    string  StageName,
    int     AcademicGroupId,
    string  AcademicGroupLabel,
    string  Label,
    int     StudentAssignmentCount,
    int     SlotAssignmentCount,
    bool    IsSchedulePublished,
    int     AcademicYearId,
    string  AcademicYearLabel,
    string? RotationGroup);

public sealed record CohortDetailResponse(
    int    Id,
    int    StageId,
    string StageName,
    int    AcademicGroupId,
    string AcademicGroupLabel,
    string Label,
    int    StudentAssignmentCount,
    bool   IsSchedulePublished,
    IReadOnlyList<CohortSlotDetail> SlotAssignments);

public sealed record CohortSlotDetail(
    int      AssignmentId,
    int      StageSlotId,
    int      PeriodNumber,
    string?  PeriodLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    int      ServiceId,
    string   ServiceName,
    string   HospitalName);
