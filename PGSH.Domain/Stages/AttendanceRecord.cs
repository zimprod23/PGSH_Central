namespace PGSH.Domain.Stages;

public sealed class AttendanceRecord
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public AttendanceStatus Status { get; set; }

    // Link to the specific rotation period
    public Guid ServicePeriodId { get; set; }
    public ServicePeriod ServicePeriod { get; set; }
}

public enum AttendanceStatus
{
    Present,
    Absent,
    JustifiedAbsent,
    Late
}