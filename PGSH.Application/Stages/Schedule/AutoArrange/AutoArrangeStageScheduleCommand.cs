using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Schedule.AutoArrange;

public sealed record AutoArrangeStageScheduleCommand(
    int StageId,
    int? AcademicYearId = null,
    int? PartitionCount = null,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null) : ICommand<AutoArrangeResult>;

public sealed record AutoArrangeResult(int Assigned, int SaturatedServices, int TotalStudents, int TotalCapacity);
