using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Attendance.Generate;

public sealed record GenerateAttendanceCommand(Guid ServicePeriodId) : ICommand<int>;
