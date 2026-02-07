using PGSH.Domain.Hospitals;

namespace PGSH.Domain.Stages;

public sealed class ServicePeriod
{
    public Guid Id { get; set; }
    public Guid InternshipAssignmentId { get; set; }
    public InternshipAssignment InternshipAssignment { get; set; }
    public int ServiceId { get; set; }
    public Service Service { get; set; }

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
    public ServicePeriod ServicePeriod { get; set; }
    public decimal TotalScore { get; set; }
    public string? SupervisorComment { get; set; }
    public ICollection<ObjectiveScore> ObjectiveScores { get; set; } = new List<ObjectiveScore>();
}

public sealed class ObjectiveScore
{
    public Guid Id { get; set; }

    // Link to the Evaluation "Form"
    public Guid ServiceEvaluationId { get; set; }
    public ServiceEvaluation ServiceEvaluation { get; set; }

    // Link to the specific requirement from the Stage
    public int StageObjectiveId { get; set; }
    public StageObjective StageObjective { get; set; }

    // The actual grade given by the Service Chief
    public int Score { get; set; }
    public string? Note { get; set; } // Specific feedback for this objective
}