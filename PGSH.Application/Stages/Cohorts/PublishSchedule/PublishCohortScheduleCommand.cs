using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

public sealed record PublishCohortScheduleCommand(int CohortId, bool AllowOverCapacity = false) : ICommand;
