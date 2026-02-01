using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed class InternshipAssignment : Entity
{
    public Guid Id { get; set; }

    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }

    //To change later
    public InternshipStatus Status { get; set; } = InternshipStatus.Planned;

    public Guid RegistrationId { get; set; }
    public Registration Registration { get; set; }

    public int StageGroupId { get; set; }
    public StageGroup StageGroup { get; set; }

    // Student-specific execution
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; }
        = new List<AttendanceRecord>();

    public ICollection<PeriodEvaluation> PeriodEvaluations { get; set; }
        = new List<PeriodEvaluation>();

    // Derived values (stored, not authoritative)
    public decimal? FinalScore { get; private set; }
    public StageAssignmentResult? Result { get; private set; }
}


public enum StageAssignmentResult
{
    NonÉvalué,
    Validé,
    NonValidé
}

