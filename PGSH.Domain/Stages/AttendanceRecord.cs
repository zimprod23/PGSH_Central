using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Domain.Stages;

public sealed class AttendanceRecord
{
    public Guid Id { get; set; }

    public DateOnly Date { get; set; }
    public AttendanceStatus Status { get; set; }

    public Guid InternshipAssignmentId { get; set; }
    public InternshipAssignment InternshipAssignment { get; set; }

    // Optional (rotation-aware)
    public Guid? StageGroupPeriodId { get; set; }
    public StageGroupPeriod? StageGroupPeriod { get; set; }
}
public enum AttendanceStatus
{
    Present,
    Absent,
    JustifiedAbsent,
    Late
}
