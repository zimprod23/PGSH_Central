namespace PGSH.Application.Stages.Attendance;

public sealed record AttendanceResponse(
    Guid Id,
    DateOnly Date,
    string Status);
