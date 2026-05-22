using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Attendance.Record;

public sealed record RecordAttendanceCommand(
    Guid ServicePeriodId,
    DateOnly Date,
    AttendanceStatus Status) : ICommand<Guid>;
