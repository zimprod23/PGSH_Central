using PGSH.Domain.Hospitals;

namespace PGSH.Domain.Stages;

public sealed class ServicePeriod
{
    public Guid Id { get; set; }
    public Guid InternshipAssignmentId { get; set; }
    public InternshipAssignment InternshipAssignment { get; set; } = default!;
    public int ServiceId { get; set; }
    public Service Service { get; set; } = default!;

    // Links to the schedule grid cell that generated this period (null = ad-hoc)
    public int? CohortSlotAssignmentId { get; set; }
    public CohortSlotAssignment? CohortSlotAssignment { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsComplete { get; set; }

    public ICollection<AttendanceRecord> Attendance { get; set; } = new List<AttendanceRecord>();
    public ServiceEvaluation? Evaluation { get; set; }
}

public sealed class ServiceEvaluation
{
    public Guid Id { get; set; }
    public Guid ServicePeriodId { get; set; }
    public ServicePeriod ServicePeriod { get; set; } = default!;
    public decimal TotalScore { get; set; }
    public string? SupervisorComment { get; set; }
    public ICollection<ObjectiveScore> ObjectiveScores { get; set; } = new List<ObjectiveScore>();
}

public sealed class ObjectiveScore
{
    public Guid Id { get; set; }
    public Guid ServiceEvaluationId { get; set; }
    public ServiceEvaluation ServiceEvaluation { get; set; } = default!;
    public int StageObjectiveId { get; set; }
    public StageObjective StageObjective { get; set; } = default!;
    public int Score { get; set; }
    public string? Note { get; set; }
}
