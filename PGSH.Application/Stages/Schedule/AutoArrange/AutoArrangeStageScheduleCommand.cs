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
/// <remarks>
/// ⚠ <b>This is a second shape of <c>RotationArrangeResult</c>, and that is exactly how a number
/// goes missing.</b> Extending the arranger's record left this one behind, so
/// <c>PinnedCellsKept</c> and <c>ReservedServices</c> were computed, carried through the handler and
/// then dropped at the boundary — the API answered without them and the screen could not have shown
/// them whatever it did. Found by driving the real screen on 2026-09-08; invisible to every handler
/// test, which reads the arranger's record directly. <b>Any field added to
/// <c>RotationArrangeResult</c> has to be added here too.</b>
/// </remarks>
/// <param name="PinnedCellsKept">
/// Cells the run left exactly as they were because a human had pinned them.
/// </param>
/// <param name="ReservedServices">
/// Authorised services withheld from the rotation because they are held for named rosters. Their
/// capacity left <paramref name="TotalCapacity"/> with them.
/// </param>
public sealed record AutoArrangeResult(
    int Assigned,
    int SaturatedServices,
    int TotalStudents,
    int TotalCapacity,
    int GroupConflicts,
    int PinnedCellsKept,
    int ReservedServices);
