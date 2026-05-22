namespace PGSH.Application.Stages.Cohorts.GetById;

public sealed record CohortResponse(
    int    Id,
    int    StageId,
    string StageName,
    int    AcademicGroupId,
    string AcademicGroupLabel,
    string Label,
    int    RotationTemplateCount,
    int    StudentAssignmentCount);

public sealed record CohortDetailResponse(
    int    Id,
    int    StageId,
    string StageName,
    int    AcademicGroupId,
    string AcademicGroupLabel,
    string Label,
    int    StudentAssignmentCount,
    IReadOnlyList<RotationTemplateResponse> RotationTemplates);

public sealed record RotationTemplateResponse(
    int      Id,
    int      ServiceId,
    string   ServiceName,
    string   HospitalName,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    int      SequenceOrder);
