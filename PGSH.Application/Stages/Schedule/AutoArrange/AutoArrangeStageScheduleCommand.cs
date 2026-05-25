using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Schedule.AutoArrange;

public sealed record AutoArrangeStageScheduleCommand(int StageId, int? PartitionCount = null) : ICommand<AutoArrangeResult>;

public sealed record AutoArrangeResult(int Assigned, int SaturatedServices, int TotalStudents, int TotalCapacity);
