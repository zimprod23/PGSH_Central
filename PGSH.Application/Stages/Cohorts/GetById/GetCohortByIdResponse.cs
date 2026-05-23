namespace PGSH.Application.Stages.Cohorts.GetById;

public sealed record CohortResponse(
    int    Id,
    int    StageId,
    string StageName,
    int    AcademicGroupId,
    string AcademicGroupLabel,
    string Label,
    int?   RotationPlanId,
    int    StudentAssignmentCount);

public sealed record CohortDetailResponse(
    int    Id,
    int    StageId,
    string StageName,
    int    AcademicGroupId,
    string AcademicGroupLabel,
    string Label,
    int    StudentAssignmentCount,
    int?   RotationPlanId,
    IReadOnlyList<RotationSlotResponse> RotationSlots);

public sealed record RotationSlotResponse(
    int      Id,
    int      ServiceId,
    string   ServiceName,
    string   HospitalName,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    int      SequenceOrder);
