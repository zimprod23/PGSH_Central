using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.UnpublishSchedule;

public sealed record UnpublishCohortScheduleCommand(int CohortId) : ICommand<int>;
