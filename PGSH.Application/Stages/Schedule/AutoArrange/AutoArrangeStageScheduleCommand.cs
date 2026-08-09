using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Schedule.AutoArrange;

public sealed record AutoArrangeStageScheduleCommand(
    int StageId,
    int? AcademicYearId = null,
    int? PartitionCount = null,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null) : ICommand<AutoArrangeResult>;

/// <summary>
/// <paramref name="GroupConflicts"/> counts cells left unwritten because the group was already
/// placed in an overlapping period of another stage — almost always an arrange run across every
/// partition where it should have targeted one. Surfaced because a run that quietly writes nothing
/// looks like it worked.
/// </summary>
public sealed record AutoArrangeResult(
    int Assigned, int SaturatedServices, int TotalStudents, int TotalCapacity, int GroupConflicts);
