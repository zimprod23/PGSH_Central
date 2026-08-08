using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

public sealed record PublishStageScheduleCommand(
    int StageId,
    int? AcademicYearId = null,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null,
    bool AllowOverCapacity = false) : ICommand<PublishResult>;
