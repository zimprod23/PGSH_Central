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

    // A transfer re-materialises periods against the target group's schedule and hands the student the
    // group's CURRENT lifecycle state per slot (the rescheduler sets IsStarted/IsComplete directly). Bring
    // the assignment's own status back in line with its periods afterwards — otherwise it stays "Planned"
    // while actually underway (hidden from the chef worklist, skipped by the bulk clôture that only closes
    // Ongoing assignments) or "Ongoing" after joining a group whose stage is already closed (never reaching
    // Completed, so its evaluations can never roll up to Evaluated). Terminal admin verdicts are left alone.
    public void SyncStatusAfterReschedule(DateOnly date)
    {
        if (Status is InternshipStatus.Evaluated
                   or InternshipStatus.Validated
                   or InternshipStatus.Rejected)
            return;

        var graded = ServicePeriods.Where(p => !p.IsInterrupted).ToList();
        if (graded.Count == 0)
            return;

        if (graded.All(p => p.IsComplete))
        {
            if (Status != InternshipStatus.Completed)
            {
                Status = InternshipStatus.Completed;
                // Joining a group whose stage is already finished ends the loan just like a natural close.
                EndTemporaryTransferIfAny(date);
            }
        }
        else if (graded.Any(p => p.IsStarted))
        {
            Status = InternshipStatus.Ongoing;
        }
        else
        {
            Status = InternshipStatus.Planned;
        }
    }

    /// <summary>
    /// Undoes a publication: drops the execution records this assignment received from the planning
    /// grid and brings its status and note back in line with what is left. Returns how many periods
    /// were removed.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Ad-hoc periods are never touched.</b> A period with no cell behind it is a délocalisation,
    /// a revalidation or an imported historical stage — none of which came from a répartition, and
    /// none of which can be reproduced by publishing one again. Unpublishing is the inverse of
    /// publishing and nothing more.
    /// <para>The status must be recomputed rather than left alone: an assignment whose periods have
    /// all just been deleted would otherwise keep reading <c>Ongoing</c> (hidden from nothing,
    /// counted by the bulk clôture) or even <c>Validated</c> with a <c>FinalScore</c> and no period
    /// behind it.</para>
    /// </remarks>
    public int RemovePublishedPeriods()
    {
        var published = ServicePeriods.Where(p => p.CohortSlotAssignmentId is not null).ToList();
        if (published.Count == 0)
            return 0;

        foreach (var period in published)
            ServicePeriods.Remove(period);

        RecomputeFinalScore();
        RecomputeStatusFromPeriods();
        return published.Count;
    }

    /// <summary>
    /// Derives the lifecycle status from the periods that actually exist. Unlike
    /// <see cref="SyncStatusAfterReschedule"/> this does <b>not</b> preserve terminal states: it is
    /// called when periods have been removed, and a verdict pronounced over evaluations that no
    /// longer exist is exactly what has to be walked back. A ratified stage whose evidence survives
    /// intact keeps its ratification.
    /// </summary>
    private void RecomputeStatusFromPeriods()
    {
        var graded = ServicePeriods.Where(p => !p.IsInterrupted).ToList();

        if (graded.Count == 0)
        {
            Status = InternshipStatus.Planned;
            return;
        }

        bool allEvaluated = graded.All(p => p.Evaluation is not null);
        bool allComplete  = graded.All(p => p.IsComplete);

        // A ratification (or refusal) survives only while every period behind it is still evaluated;
        // it is an administrative act on a complete record, not on whichever periods remain.
        if (Status is InternshipStatus.Validated or InternshipStatus.Rejected && allEvaluated)
            return;

        Status = allEvaluated && allComplete ? InternshipStatus.Evaluated
               : allComplete                 ? InternshipStatus.Completed
               : graded.Any(p => p.IsStarted) ? InternshipStatus.Ongoing
               : InternshipStatus.Planned;
    }

    // Suspends an in-flight period (e.g. an exam week). Only a started, not-yet-complete period
    // can be paused; the chef sees it frozen until an admin resumes it.
    public Result PausePeriod(Guid periodId, DateOnly date, PauseKind kind, string? reason)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == periodId);
        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(periodId));
        if (period.IsInterrupted)
            return AppResult.Failure(StageErrors.PeriodInterrupted(periodId));
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

        // Only rotations still ahead of the student move. A closed rotation is history — its dates are
        // what actually happened — and an interrupted one is terminal, so pushing either forward would
        // rewrite the past to make room for time lost in the present.
        foreach (var later in ServicePeriods.Where(p => p.Id != period.Id
                                                     && p.StartDate > period.StartDate
                                                     && !p.IsComplete
                                                     && !p.IsInterrupted))
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
        if (period.IsInterrupted)
            return AppResult.Failure(StageErrors.PeriodInterrupted(periodId));
        if (period.IsComplete)
            return AppResult.Failure(StageErrors.PeriodAlreadyComplete(periodId));
        if (period.IsPaused)
            return AppResult.Failure(StageErrors.PeriodPaused(periodId));
        // Symmetric with PausePeriod: a rotation nobody ever began cannot be closed, and closing one
        // is what makes it evaluable — without this a stage that never ran could still be graded.
        if (!period.IsStarted)
            return AppResult.Failure(StageErrors.PeriodNotStarted(periodId));

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
        Raise(new EvaluationSubmittedDomainEvent(
            Id, RegistrationId, periodId, StageScoring.PeriodMark(evaluation)));

        if (Status == InternshipStatus.Completed
            && ServicePeriods.All(p => p.Evaluation is not null || p.IsInterrupted))
            Status = InternshipStatus.Evaluated;

        return AppResult.Success();
    }

    /// <summary>
    /// Corrects a mark already on record. Goes through the aggregate for the same reason a first
    /// submission does: the stage note has to be recomputed from the amended marks, and a grade change
    /// must leave an audit trail. Refused once the administration has ratified or refused the stage.
    /// </summary>
    public Result AmendEvaluation(Guid periodId, EvaluationAmendment amendment)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == periodId);
        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(periodId));
        if (period.Evaluation is null)
            return AppResult.Failure(StageErrors.EvaluationNotFound(periodId));
        if (Status is InternshipStatus.Validated or InternshipStatus.Rejected)
            return AppResult.Failure(StageErrors.EvaluationReadOnly(Status));

        var evaluation = period.Evaluation;
        decimal previousMark = StageScoring.PeriodMark(evaluation);

        evaluation.Mode              = amendment.Mode;
        evaluation.TotalScore        = amendment.TotalScore;
        evaluation.Outcome           = amendment.Outcome;
        evaluation.SupervisorComment = amendment.SupervisorComment;
        evaluation.FicheReference    = amendment.FicheReference;
        evaluation.EvaluatedByUserId = amendment.EvaluatedByUserId;
        evaluation.EvaluatedAt       = amendment.EvaluatedAt;

        // Replace the per-objective marks in place. Clearing the tracked collection deletes the old
        // rows (required FK + cascade), and the replacements must NOT carry a pre-set Id — on an
        // already-tracked evaluation a non-sentinel store-generated key makes EF classify them
        // Modified instead of Added (UPDATE a non-existent row → DbUpdateConcurrencyException).
        // Same gotcha as Delocalize and TransferToCohort below.
        evaluation.ObjectiveScores.Clear();
        foreach (var score in amendment.ObjectiveScores)
            evaluation.ObjectiveScores.Add(score);

        evaluation.Normalize();
        RecomputeFinalScore();
        Raise(new EvaluationAmendedDomainEvent(
            Id, RegistrationId, periodId, evaluation.Id,
            previousMark, StageScoring.PeriodMark(evaluation)));

        return AppResult.Success();
    }

    /// <summary>
    /// Ratifies the chef's evaluation: the marks become official. This is a workflow act, not an
    /// academic one — <see cref="Result"/> stays whatever the marks produced, so ratifying a failed
    /// stage records an official failure rather than converting it into a pass.
    /// </summary>
    public Result Validate()
    {
        if (Status != InternshipStatus.Evaluated)
            return AppResult.Failure(StageErrors.InvalidStatusTransition("Validate", Status));

        Status = InternshipStatus.Validated;
        Raise(new AssignmentValidatedDomainEvent(Id, RegistrationId, FinalScore));
        return AppResult.Success();
    }

    /// <summary>
    /// Refuses to ratify: the chef's evaluation is not accepted as official (contested, incomplete,
    /// entered against the wrong student…). Like <see cref="Validate"/> this moves the workflow only
    /// — the marks on record keep producing <see cref="Result"/> until they are actually amended.
    /// </summary>
    public Result Reject()
    {
        if (Status != InternshipStatus.Evaluated)
            return AppResult.Failure(StageErrors.InvalidStatusTransition("Reject", Status));

        Status = InternshipStatus.Rejected;
        Raise(new AssignmentRejectedDomainEvent(Id, RegistrationId));
        return AppResult.Success();
    }

    // ─── Délocalisation ──────────────────────────────────────────────────────

    /// <summary>
    /// What a délocalisation would cost on this assignment, without performing one. The bulk preview
    /// reads this so the operator sees the damage before authorising it, and reads it through the
    /// same expressions <see cref="Delocalize"/> then enforces — a preview computed one way and a
    /// guard written another is two rules with nothing to catch them disagreeing.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Every field here reads <c>ServicePeriod.Evaluation</c></b>, so the navigation has to be
    /// loaded. An un-Included evaluation is indistinguishable from an absent one, and the answer it
    /// produces then is « rien à perdre » on a stage that carries a mark.
    /// </remarks>
    public DelocalizationPreflight PreflightDelocalization() => new(
        AlreadyDelocalized: ServicePeriods.Any(p => p.IsDelocalized),
        MarkedPeriods:      ServicePeriods.Count(p => p.Evaluation is not null),
        DroppedPeriods:     ServicePeriods.Count,
        // ⚠ « not merely Planned », asked through the lifecycle rather than by restating its flags.
        // A fifth hand-written triple beside the four the class owns is the drift it exists to stop —
        // and the class is also right about the edge this would have missed: a period complete but
        // never started is movement the store can hold, and it is not Planned.
        UnderwayPeriods:    ServicePeriods.Count(p =>
            p.Evaluation is null && !ServicePeriodLifecycle.IsPlanned(p)));

    /// <summary>
    /// The whole stage is served outside the faculty. The in-faculty rotation never happens, so the
    /// periods it produced are dropped and replaced by a single ad-hoc period at the external
    /// service — created already started + complete, since the stage is done by the time anyone
    /// records it. The verdict is entered afterwards, or in the same act by the caller.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>The one thing it refuses is a mark.</b> It used to refuse as soon as any period had
    /// <i>begun</i>, which reads as the safer rule and is not the useful one: a student leaves for an
    /// external hospital mid-rotation, and our dates are a formality the place he actually goes to
    /// does not follow — what comes back is a verdict, not a schedule. So a started period is dropped
    /// like a planned one. An <b>evaluated</b> one is not: a mark is the single thing here that
    /// nothing puts back, and no bulk act may be able to erase one.</para>
    ///
    /// <para>Délocalising a stage already délocalisé therefore replaces it, which is what makes the
    /// same list safe to re-send after a correction.</para>
    /// </remarks>
    public Result Delocalize(int stageId, int serviceId, DateOnly startDate, DateOnly endDate,
        string reason, Guid? demandeId)
    {
        if (ServicePeriods.Any(p => p.Evaluation is not null))
            return AppResult.Failure(StageErrors.DelocalizationOverMark);

        foreach (var existing in ServicePeriods.ToList())
            ServicePeriods.Remove(existing);

        // Do NOT pre-set the Id of the period or its Delocalization child: when this assignment is
        // already tracked, a non-sentinel store-generated key makes EF classify the child as
        // Modified (UPDATE a non-existent row) instead of Added. Let EF generate the keys.
        ServicePeriods.Add(new ServicePeriod
        {
            InternshipAssignmentId = Id,
            ServiceId              = serviceId,
            CohortSlotAssignmentId = null,
            StartDate              = startDate,
            EndDate                = endDate,
            IsStarted              = true,
            IsComplete             = true,
            IsDelocalized          = true,
            Delocalization         = new Delocalization { Reason = reason, DemandeId = demandeId },
        });

        // The dropped periods may have carried nothing, but FinalScore is stored rather than derived
        // on read, and an assignment whose evidence has just been deleted must not keep a note.
        RecomputeFinalScore();
        Status = InternshipStatus.Completed;
        Raise(new StudentDelocalizedDomainEvent(Id, RegistrationId, stageId, serviceId, reason));
        return AppResult.Success();
    }

    /// <summary>
    /// Walks a délocalisation back: removes the ad-hoc period and returns the student to the
    /// répartition. The planning grid was never touched by the délocalisation itself, so where the
    /// cohort still holds its cells a re-publication restores the in-faculty rotation; where they
    /// were cleared the student lands in « non réparti », which is the truth about him.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Refused once the paper verdict is on record.</b> That mark is the only trace the faculty
    /// holds of a stage nobody here supervised — there is no chef to ask again and no attendance
    /// behind it. Correcting it is <see cref="AmendEvaluation"/>'s job; undoing the délocalisation
    /// would delete it.
    /// </remarks>
    public Result CancelDelocalization(int stageId)
    {
        var delocalized = ServicePeriods.Where(p => p.IsDelocalized).ToList();

        if (delocalized.Count == 0)
            return AppResult.Failure(StageErrors.NotDelocalized);

        if (delocalized.Any(p => p.Evaluation is not null))
            return AppResult.Failure(StageErrors.DelocalizationAlreadyMarked);

        int serviceId = delocalized[0].ServiceId;

        foreach (var period in delocalized)
            ServicePeriods.Remove(period);

        RecomputeFinalScore();
        RecomputeStatusFromPeriods();
        Raise(new DelocalizationCancelledDomainEvent(Id, RegistrationId, stageId, serviceId));
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

    /// <summary>
    /// « Changement de groupe », affectation side: this affectation <b>is</b>, and always was, in
    /// <paramref name="newCohortId"/>. Re-points the affectation and rewrites its open membership row
    /// in place — no closing date, no second row, no event.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Rewriting the open row is what leaves no trace; keeping the closed ones is what
    /// keeps the act honest.</b> <see cref="TransferToCohort"/> closes the current membership and opens
    /// another, which is right for a move — the student was in one cohorte until a date and in another
    /// afterwards — and is exactly the trace a correction must not leave. But a membership already
    /// closed records a transfer that <i>did</i> happen, and this act has no business erasing it: the
    /// dossier then reads as though the student went straight from the roster he really left to the one
    /// he is really in, which is the truth once this correction is applied.</para>
    ///
    /// <para><b>The refusal is on <see cref="Status"/>, and only on it.</b> A correction is truthful
    /// only while the affectation is still a plan; past <see cref="InternshipStatus.Planned"/> there is
    /// a rotation, a mark or a verdict that says the student stood somewhere, and « il n'y a jamais été »
    /// stops being a correction and becomes a falsification. ⚠ The deeper facts — a période started, a
    /// mark entered, a day of attendance — are deliberately <b>not</b> counted here: they hang off
    /// collections an un-<c>Include</c>d load reports as empty, and an aggregate that answers « rien
    /// enregistré » to a caller who forgot one would wave through exactly the case it exists to stop.
    /// The store is asked for those and the caller is refused before it gets here — the division
    /// <c>CnpnSpanFloor</c> already makes. <see cref="Status"/> is a scalar, always loaded, so the half
    /// the aggregate can see without ambiguity is the half it owns.</para>
    /// </remarks>
    public Result ReassignToCohort(int newCohortId)
    {
        if (Status != InternshipStatus.Planned)
            return AppResult.Failure(StageErrors.AffectationNotCorrectable(Id, Status));

        if (CurrentCohortId == newCohortId)
            return AppResult.Success();

        var active = MembershipHistory.FirstOrDefault(m => m.EndDate is null);

        if (active is not null)
        {
            active.CohortId = newCohortId;
            // A loan's return address named the cohorte this affectation is no longer in. Left as it
            // was, the auto-revert would send the student back to a roster the record no longer says
            // he came from.
            active.OriginalCohortId = null;
        }

        CurrentCohortId = newCohortId;
        return AppResult.Success();
    }

    // ─── Score computation ────────────────────────────────────────────────────

    public void RecalculateFinalScore() => RecomputeFinalScore();

    // The stage note is the mean of the periods' marks; the stage passes only when EVERY period
    // passes individually — a single failed period fails the whole stage — and only once every
    // (non-interrupted) period has been evaluated. Per-period mark/verdict rules live in StageScoring
    // so the read handlers (student record + fiche de validation) compute the same numbers.
    private void RecomputeFinalScore()
    {
        var gradedPeriods = ServicePeriods.Where(p => !p.IsInterrupted).ToList();
        var evaluations = gradedPeriods
            .Where(p => p.Evaluation is not null)
            .Select(p => p.Evaluation!)
            .ToList();

        if (evaluations.Count == 0)
        {
            FinalScore = null;
            Result = StageAssignmentResult.NonÉvalué;
            return;
        }

        FinalScore = Math.Round(evaluations.Average(StageScoring.PeriodMark), 2);

        // The verdict is meaningful only when the whole stage is graded; until then it stays
        // "not evaluated" even though a partial mean is already shown. Nothing else ever writes it:
        // ratifying or refusing an evaluation moves Status only, so the academic outcome always
        // traces back to the marks the chef actually gave.
        bool allEvaluated = gradedPeriods.All(p => p.Evaluation is not null);
        Result = !allEvaluated
            ? StageAssignmentResult.NonÉvalué
            : evaluations.All(StageScoring.IsPeriodValidated)
                ? StageAssignmentResult.Validé
                : StageAssignmentResult.NonValidé;
    }
}

public enum StageAssignmentResult
{
    NonÉvalué,
    Validé,
    NonValidé
}

