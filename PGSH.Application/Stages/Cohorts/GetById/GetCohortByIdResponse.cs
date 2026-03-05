namespace PGSH.Application.Stages.Cohorts.GetById;

public sealed record CohortResponse(
    int Id,
    int StageId,
    string StageName,
    string Label,
    int RotationTemplateCount,
    int StudentAssignmentCount);
