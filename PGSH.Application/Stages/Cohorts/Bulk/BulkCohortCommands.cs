using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.Bulk;

public sealed record StartCohortAssignmentsCommand(int CohortId) : ICommand<int>;
public sealed record CompletePeriodsCommand(int CohortId) : ICommand<int>;
public sealed record ValidateCohortAssignmentsCommand(int CohortId) : ICommand<int>;
