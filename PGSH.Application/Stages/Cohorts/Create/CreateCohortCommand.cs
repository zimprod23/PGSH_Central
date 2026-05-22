using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.Create;

public record CreateCohortCommand(
    int StageId,
    int AcademicGroupId,
    string Label) : ICommand<int>;