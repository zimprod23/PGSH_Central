using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Manage.Schedule;

public sealed record GenerateScheduleCommand(
    int AcademicYearId,
    List<StageScheduleRequest> Stages,
    List<int> AvailableServiceIds) : ICommand<BulkResponse<int, Guid>>;

public sealed record StageScheduleRequest(
    int StageId,
    DateOnly GlobalStartDate,
    int NumberOfRotations,
    int RotationDurationDays);