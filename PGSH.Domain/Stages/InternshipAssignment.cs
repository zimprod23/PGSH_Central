using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;
using AppResult = PGSH.SharedKernel.Result;

namespace PGSH.Domain.Stages;

public sealed class InternshipAssignment : Entity
{
    public Guid Id { get; set; }
    public InternshipStatus Status { get; private set; } = InternshipStatus.Planned;

    public Guid RegistrationId { get; set; }
    public Registration Registration { get; set; }

    public int CurrentCohortId { get; set; }
    public Cohort Cohort { get; set; }

    public ICollection<ServicePeriod> ServicePeriods { get; set; } = new List<ServicePeriod>();
    public ICollection<CohortMembership> MembershipHistory { get; set; } = new List<CohortMembership>();

    public decimal? FinalScore { get; private set; }
    public StageAssignmentResult? Result { get; private set; } = StageAssignmentResult.NonÉvalué;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    public Result Start()
    {
        if (Status != InternshipStatus.Planned)
            return AppResult.Failure(StageErrors.InvalidStatusTransition("Start", Status));
        Status = InternshipStatus.Ongoing;
        // Whole-student start: activate every period so the relevant chefs can manage them.
        foreach (var period in ServicePeriods) period.IsStarted = true;
        return AppResult.Success();
    }

    // Activates a single period (period-scoped start). The assignment becomes Ongoing as soon
    // as any of its periods is started; future periods stay inactive until started in turn.
    public Result StartPeriod(Guid periodId)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == periodId);
        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(periodId));
        if (period.IsStarted)
            return AppResult.Failure(StageErrors.PeriodAlreadyStarted(periodId));

        period.IsStarted = true;
        if (Status == InternshipStatus.Planned) Status = InternshipStatus.Ongoing;
        return AppResult.Success();
    }

    // Suspends an in-flight period (e.g. an exam week). Only a started, not-yet-complete period
    // can be paused; the chef sees it frozen until an admin resumes it.
    public Result PausePeriod(Guid periodId, DateOnly date, PauseKind kind, string? reason)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == periodId);
        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(periodId));
        if (!period.IsStarted)
            return AppResult.Failure(StageErrors.PeriodNotStarted(periodId));
        if (period.IsComplete)
            return AppResult.Failure(StageErrors.PeriodAlreadyComplete(periodId));
        if (period.IsPaused)
            return AppResult.Failure(StageErrors.PeriodAlreadyPaused(periodId));

        period.IsPaused = true;
        period.Pauses.Add(new PeriodPause
        {
            ServicePeriodId = period.Id,
            StartDate       = date,
            Kind            = kind,
            Reason          = reason,
        });
        return AppResult.Success();
    }

    // Resumes a paused period: the days lost while paused extend this period's end, then every
    // later period of this assignment is pushed forward by the same amount so the rotation stays
    // contiguous and the student still serves each stage in full.
    public Result ResumePeriod(Guid periodId, DateOnly date)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == periodId);
        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(periodId));
        if (!period.IsPaused)
            return AppResult.Failure(StageErrors.PeriodNotPaused(periodId));

        var openPause = period.Pauses.FirstOrDefault(p => p.ResumeDate is null);
        period.IsPaused = false;
        if (openPause is null)
            return AppResult.Success();

        openPause.ResumeDate = date;
        int days = date.DayNumber - openPause.StartDate.DayNumber;
        if (days <= 0)
            return AppResult.Success();

        period.EndDate = period.EndDate.AddDays(days);
        foreach (var later in ServicePeriods.Where(p => p.Id != period.Id && p.StartDate > period.StartDate))
        {
            later.StartDate = later.StartDate.AddDays(days);
            later.EndDate   = later.EndDate.AddDays(days);
        }

        return AppResult.Success();
    }

    public Result CompletePeriod(Guid periodId)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == periodId);
        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(periodId));
        if (period.IsComplete)
            return AppResult.Failure(StageErrors.PeriodAlreadyComplete(periodId));
        if (period.IsPaused)
            return AppResult.Failure(StageErrors.PeriodPaused(periodId));

        period.IsComplete = true;
        Raise(new ServicePeriodCompletedDomainEvent(Id, periodId));

        // Interrupted periods (cut short by a forced mid-stage transfer) are terminal — they
        // never complete, so they must not hold the stage open.
        if (Status == InternshipStatus.Ongoing && ServicePeriods.All(p => p.IsComplete || p.IsInterrupted))
        {
            Status = InternshipStatus.Completed;
            EndTemporaryTransferIfAny(DateOnly.FromDateTime(DateTime.UtcNow));
        }

        return AppResult.Success();
    }

    // When the stage this assignment belongs to finishes, a student who was on a temporary
    // loan to another group returns home: close the temporary membership and audit the return.
    // Remaining stages were never moved, so nothing else needs undoing.
    private void EndTemporaryTransferIfAny(DateOnly date)
    {
        var active = MembershipHistory.FirstOrDefault(m => m.EndDate is null);
        if (active is null
            || active.TransferType != TransferType.Temporary
            || active.OriginalCohortId is null)
            return;

        active.EndDate = date;
        Raise(new TemporaryTransferEndedDomainEvent(
            Id, RegistrationId, active.CohortId, active.OriginalCohortId.Value, active.TransferReason));
    }

    public Result SubmitEvaluation(Guid periodId, ServiceEvaluation evaluation)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == periodId);
        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(periodId));
        if (!period.IsComplete)
            return AppResult.Failure(StageErrors.PeriodNotComplete(periodId));
        if (period.Evaluation is not null)
            return AppResult.Failure(StageErrors.EvaluationAlreadyExists(periodId));

        evaluation.Normalize();
        period.Evaluation = evaluation;
        RecomputeFinalScore();
        Raise(new EvaluationSubmittedDomainEvent(Id, RegistrationId, periodId, evaluation.TotalScore));

        if (Status == InternshipStatus.Completed
            && ServicePeriods.All(p => p.Evaluation is not null || p.IsInterrupted))
            Status = InternshipStatus.Evaluated;

        return AppResult.Success();
    }

    public Result Validate()
    {
        if (Status != InternshipStatus.Evaluated)
            return AppResult.Failure(StageErrors.InvalidStatusTransition("Validate", Status));
        Status = InternshipStatus.Validated;
        Result = StageAssignmentResult.Validé;
        Raise(new AssignmentValidatedDomainEvent(Id, RegistrationId, FinalScore));
        return AppResult.Success();
    }

    public Result Reject()
    {
        if (Status != InternshipStatus.Evaluated)
            return AppResult.Failure(StageErrors.InvalidStatusTransition("Reject", Status));
        Status = InternshipStatus.Rejected;
        Result = StageAssignmentResult.NonValidé;
        Raise(new AssignmentRejectedDomainEvent(Id, RegistrationId));
        return AppResult.Success();
    }

    // ─── Cohort transfer ─────────────────────────────────────────────────────

    public void TransferToCohort(int newCohortId, string? reason, DateOnly date,
        TransferType type = TransferType.Definitive)
    {
        var active = MembershipHistory.FirstOrDefault(m => m.EndDate is null);
        if (active is not null) active.EndDate = date;

        int previousCohortId = CurrentCohortId;

        // Do NOT pre-set Id: this membership is added to an already-tracked assignment, so a
        // non-sentinel store-generated key makes EF classify it as Modified (UPDATE a non-existent
        // row → DbUpdateConcurrencyException) instead of Added. Let EF generate the key.
        MembershipHistory.Add(new CohortMembership
        {
            InternshipAssignmentId = Id,
            CohortId               = newCohortId,
            StartDate              = date,
            TransferReason         = reason,
            TransferType           = type,
            // A temporary loan remembers where to return; a definitive move does not.
            OriginalCohortId       = type == TransferType.Temporary ? previousCohortId : null,
        });

        CurrentCohortId = newCohortId;

        Raise(new StudentCohortTransferredDomainEvent(Id, RegistrationId, previousCohortId, newCohortId, reason, type));
    }

    // ─── Score computation ────────────────────────────────────────────────────

    public void RecalculateFinalScore() => RecomputeFinalScore();

    // Pass threshold: a final mark at or above this validates the stage.
    private const decimal ValidationThreshold = 10m;

    // A validate-only verdict maps onto the numeric scale so a chef who certifies without
    // grading still contributes a usable mark: validated = 10, not validated = 0.
    private static decimal OutcomeToScore(EvaluationOutcome? outcome) =>
        outcome == EvaluationOutcome.Validated ? 10m : 0m;

    private void RecomputeFinalScore()
    {
        var evaluations = ServicePeriods
            .Where(p => p.Evaluation is not null)
            .Select(p => p.Evaluation!)
            .ToList();

        if (evaluations.Count == 0)
        {
            FinalScore = null;
            Result = StageAssignmentResult.NonÉvalué;
            return;
        }

        decimal weightedSum = 0;
        decimal totalWeight = 0;

        foreach (var evaluation in evaluations)
        {
            switch (evaluation.Mode)
            {
                case EvaluationMode.ValidatePeriod:
                    weightedSum += OutcomeToScore(evaluation.Outcome);
                    totalWeight += 1;
                    break;

                case EvaluationMode.ValidateObjectives:
                    foreach (var o in evaluation.ObjectiveScores)
                    {
                        decimal weight = o.StageObjective?.Weight ?? 1;
                        weightedSum += OutcomeToScore(o.Outcome) * weight;
                        totalWeight += weight;
                    }
                    break;

                default: // Numeric
                    if (evaluation.ObjectiveScores.Count == 0)
                    {
                        if (evaluation.TotalScore.HasValue)
                        {
                            weightedSum += evaluation.TotalScore.Value;
                            totalWeight += 1;
                        }
                        break;
                    }
                    foreach (var o in evaluation.ObjectiveScores)
                    {
                        decimal weight = o.StageObjective?.Weight ?? 1;
                        weightedSum += (o.Score ?? 0) * weight;
                        totalWeight += weight;
                    }
                    break;
            }
        }

        FinalScore = totalWeight > 0 ? Math.Round(weightedSum / totalWeight, 2) : null;

        // Auto-derive the pass/fail result from the threshold. An admin can still override
        // it terminally via Validate()/Reject() (after which evaluations are read-only, so
        // this never runs again to clobber their decision).
        Result = FinalScore is null
            ? StageAssignmentResult.NonÉvalué
            : FinalScore >= ValidationThreshold ? StageAssignmentResult.Validé : StageAssignmentResult.NonValidé;
    }
}

public enum StageAssignmentResult
{
    NonÉvalué,
    Validé,
    NonValidé
}

