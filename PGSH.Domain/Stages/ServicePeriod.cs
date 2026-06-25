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

    // A period becomes active (visible/actionable to its service chef) only once an admin
    // starts it. Future, not-yet-started periods stay hidden from the chef worklist.
    public bool IsStarted { get; set; }
    public bool IsComplete { get; set; }

    // Set when a forced mid-stage transfer cuts this rotation short: the period is kept for
    // history (with attendance up to the transfer date) but is terminal — excluded from the
    // chef worklist, the status counts, the score, and the "all periods done" lifecycle checks.
    public bool IsInterrupted { get; set; }

    public ICollection<AttendanceRecord> Attendance { get; set; } = new List<AttendanceRecord>();
    public ServiceEvaluation? Evaluation { get; set; }
}

public sealed class ServiceEvaluation
{
    public Guid Id { get; set; }
    public Guid ServicePeriodId { get; set; }
    public ServicePeriod ServicePeriod { get; set; } = default!;

    public EvaluationMode Mode { get; set; } = EvaluationMode.Numeric;

    /// <summary>Weighted final note (0–20). Null for the validate-only modes.</summary>
    public decimal? TotalScore { get; set; }

    /// <summary>Pass/fail verdict for the validate-only modes. Null for <see cref="EvaluationMode.Numeric"/>.</summary>
    public EvaluationOutcome? Outcome { get; set; }

    public string? SupervisorComment { get; set; }
    public ICollection<ObjectiveScore> ObjectiveScores { get; set; } = new List<ObjectiveScore>();

    /// <summary>
    /// Normalizes the evaluation against its <see cref="Mode"/>: derives the period
    /// <see cref="Outcome"/> from the per-objective verdicts when validating objectives,
    /// and clears fields that don't apply to the chosen mode.
    /// </summary>
    public void Normalize()
    {
        switch (Mode)
        {
            case EvaluationMode.Numeric:
                Outcome = null;
                foreach (var o in ObjectiveScores) o.Outcome = null;
                break;

            case EvaluationMode.ValidatePeriod:
                TotalScore = null;
                ObjectiveScores.Clear();
                break;

            case EvaluationMode.ValidateObjectives:
                TotalScore = null;
                foreach (var o in ObjectiveScores) o.Score = null;
                Outcome = DeriveObjectiveOutcome();
                break;
        }
    }

    // A period is validated when every mandatory objective passes; if the stage marks none
    // mandatory, every objective must pass. Optional objectives never block on their own.
    private EvaluationOutcome DeriveObjectiveOutcome()
    {
        var gates = ObjectiveScores.Where(o => o.StageObjective?.IsMandatory == true).ToList();
        if (gates.Count == 0) gates = ObjectiveScores.ToList();

        return gates.All(o => o.Outcome == EvaluationOutcome.Validated)
            ? EvaluationOutcome.Validated
            : EvaluationOutcome.NotValidated;
    }
}

public sealed class ObjectiveScore
{
    public Guid Id { get; set; }
    public Guid ServiceEvaluationId { get; set; }
    public ServiceEvaluation ServiceEvaluation { get; set; } = default!;
    public int StageObjectiveId { get; set; }
    public StageObjective StageObjective { get; set; } = default!;

    /// <summary>Numeric grade (0–20). Null in <see cref="EvaluationMode.ValidateObjectives"/>.</summary>
    public int? Score { get; set; }

    /// <summary>Pass/fail verdict. Set only in <see cref="EvaluationMode.ValidateObjectives"/>.</summary>
    public EvaluationOutcome? Outcome { get; set; }

    public string? Note { get; set; }
}
