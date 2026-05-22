using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Attendance.GetByPeriod;

public sealed record GetAttendanceQuery(Guid ServicePeriodId) : IQuery<List<AttendanceResponse>>;
