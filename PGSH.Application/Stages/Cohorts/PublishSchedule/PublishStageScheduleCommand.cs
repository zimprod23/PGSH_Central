using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

public sealed record PublishStageScheduleCommand(
    int StageId,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null) : ICommand<PublishResult>;
