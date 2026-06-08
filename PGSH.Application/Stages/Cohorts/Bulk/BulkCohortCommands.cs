using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.Bulk;

// PeriodNumbers (optional) scopes the action to specific stage periods (P1, P2…) — e.g. close
// only the rotations whose slot is the current period. Null/empty = every period of the cohort.
public sealed record StartCohortAssignmentsCommand(int CohortId, IReadOnlyList<int>? PeriodNumbers = null) : ICommand<int>;
public sealed record CompletePeriodsCommand(int CohortId, IReadOnlyList<int>? PeriodNumbers = null) : ICommand<int>;
public sealed record ValidateCohortAssignmentsCommand(int CohortId) : ICommand<int>;
