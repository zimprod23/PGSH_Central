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

    // Temporarily suspended (e.g. an exam week mid-rotation). Non-terminal: the rotation resumes,
    // and on resume the lost days extend this period's end and push every later period forward.
    // While paused the period is shown as such to the chef but is not actionable.
    public bool IsPaused { get; set; }

    // Set when the stage is served entirely outside the faculty (the student's hometown or abroad).
    // The faculty has no control over the rotation, so this is an ad-hoc period (no slot cell, no
    // in-app chef): created already started + complete, evaluated later from the paper "fiche de
    // validation" the student brings back. The movement details live in <see cref="Delocalization"/>.
    public bool IsDelocalized { get; set; }
    public Delocalization? Delocalization { get; set; }

    public ICollection<PeriodPause> Pauses { get; set; } = new List<PeriodPause>();

    public ICollection<AttendanceRecord> Attendance { get; set; } = new List<AttendanceRecord>();
    public ServiceEvaluation? Evaluation { get; set; }
}

/// <summary>
/// A suspension of a <see cref="ServicePeriod"/> — e.g. an exam break mid-rotation. The period is
/// frozen on <see cref="StartDate"/> and runs again on <see cref="ResumeDate"/>; the elapsed days
/// are added back to the rotation so the student still serves the full stage.
/// </summary>
public sealed class PeriodPause
{
    public Guid Id { get; set; }
    public Guid ServicePeriodId { get; set; }
    public ServicePeriod ServicePeriod { get; set; } = default!;

    public DateOnly StartDate { get; set; }
    public DateOnly? ResumeDate { get; set; }
    public PauseKind Kind { get; set; } = PauseKind.Exam;
    public string? Reason { get; set; }
}

public enum PauseKind
{
    Exam,
    Holiday,
    Other,
}

/// <summary>
/// Records that a <see cref="ServicePeriod"/> was served outside the faculty (délocalisation):
/// the whole stage is done at an external service the app does not supervise. Carries the motif and
/// a placeholder link to the future Demande (Phase 5). The validation verdict + the paper-proof
/// reference are recorded later on the period's <see cref="ServiceEvaluation"/>.
/// </summary>
public sealed class Delocalization
{
    public Guid Id { get; set; }
    public Guid ServicePeriodId { get; set; }
    public ServicePeriod ServicePeriod { get; set; } = default!;

    public string Reason { get; set; } = string.Empty;

    // Nullable placeholder until the Demande microservice exists (Phase 5).
    public Guid? DemandeId { get; set; }
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

    /// <summary>The user (chef or administrator) who last recorded this evaluation, and when — audit trail.</summary>
    public Guid? EvaluatedByUserId { get; set; }
    public DateTime? EvaluatedAt { get; set; }

    /// <summary>
    /// Reference to the paper "fiche de validation" backing an evaluation recorded for a stage the
    /// app did not supervise — chiefly a délocalisation, where the external service certifies on
    /// paper and an administrator enters the verdict. A URL/external reference for now; real file
    /// upload is deferred to Phase 5 with the Demande microservice.
    /// </summary>
    public string? FicheReference { get; set; }

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
