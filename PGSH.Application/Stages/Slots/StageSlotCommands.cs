using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Slots;

public sealed record CreateStageSlotCommand(
    int      StageId,
    int      PeriodNumber,
    string?  Label,
    DateOnly StartDate,
    DateOnly EndDate) : ICommand<int>;

public sealed record UpdateStageSlotCommand(
    int      SlotId,
    int      StageId,
    string?  Label,
    DateOnly StartDate,
    DateOnly EndDate) : ICommand;

public sealed record DeleteStageSlotCommand(int SlotId) : ICommand;

public sealed record SetCohortSlotAssignmentCommand(
    int CohortId,
    int StageSlotId,
    int ServiceId) : ICommand<int>;

public sealed record ClearCohortSlotAssignmentCommand(
    int CohortId,
    int StageSlotId) : ICommand;

public sealed record ClearSlotAssignmentsCommand(int StageSlotId) : ICommand<ClearSlotResult>;

public sealed record ClearSlotResult(int Cleared, int Skipped);
