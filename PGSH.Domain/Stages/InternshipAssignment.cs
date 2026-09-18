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

    public Result CompletePeriod(Guid periodId)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == periodId);
        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(periodId));
        if (period.IsInterrupted)
            return AppResult.Failure(StageErrors.PeriodInterrupted(periodId));
        if (period.IsComplete)
            return AppResult.Failure(StageErrors.PeriodAlreadyComplete(periodId));
        // Nothing in the application can set IsPaused any more (the stage-scoped pause act was
        // retired on 18/09/2026 — a promotion declares a window, it does not push dates). The guard
        // stays because the *store* can still hold a paused row: an import reversal puts back a
        // période exactly as it stood, IsPaused included.
        if (period.IsPaused)
            return AppResult.Failure(StageErrors.PeriodPaused(periodId));
        // A rotation nobody ever began cannot be closed, and closing one is what makes it evaluable —
        // without this a stage that never ran could still be graded.
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

    /// <summary>
    /// Replaces this affectation's whole rotation with the one a person declared — the canevas des
    /// affectations. Returns how many périodes were destroyed to make room.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is an act of the aggregate and not a loop in the importer.</b> Dropping
    /// périodes moves three things at once: the stage note computed from marks that no longer exist,
    /// the lifecycle status derived from périodes that no longer exist, and — for a student on loan —
    /// the temporary membership that ends when the stage does. An importer writing rows directly
    /// would have to remember all three, and <see cref="RemovePublishedPeriods"/> already exists
    /// because forgetting them left assignments reading <c>Validated</c> with a note and nothing
    /// underneath.</para>
    ///
    /// <para>⚠ <b>Unlike <see cref="RemovePublishedPeriods"/> this takes the ad-hoc périodes too</b>,
    /// and that is the whole difference between the two. Unpublishing is the inverse of publishing, so
    /// it touches only what publishing made; this is a human overruling the record, so what it
    /// replaces is everything the record says about this stage. It is also why it is refused over a
    /// mark rather than silently skipping one.</para>
    ///
    /// <para>⚠ <b>The one thing it refuses is a mark</b> — the same bargain
    /// <see cref="Delocalize"/> makes, for the same reason: a mark is the single thing here that
    /// nothing puts back, and no bulk act may be able to erase one. The importer refuses the row
    /// earlier and more helpfully; this is the guard that holds when a mark is entered between the
    /// aperçu and the apply.</para>
    ///
    /// <para>The périodes are created <b>not started</b>: a declared rotation is a plan like any
    /// other, and the admin starts it. ⚠ They carry no <c>CohortSlotAssignmentId</c> — nothing in the
    /// grid produced them — so <see cref="RemovePublishedPeriods"/> will not take them back, and the
    /// planning grid's occupancy, which reads cells, does not count them.</para>
    /// </remarks>
    public Result<int> DeclareRotation(int stageId, IReadOnlyList<DeclaredPeriod> periods)
    {
        if (periods.Count == 0)
            return AppResult.Failure<int>(StageErrors.DeclaredRotationEmpty);

        if (ServicePeriods.Any(p => p.Evaluation is not null))
            return AppResult.Failure<int>(StageErrors.DeclaredRotationOverMark);

        // ⚠ The second thing nothing puts back, and it was missing until 13/09/2026. AttendanceRecord
        // cascades from ServicePeriod, so dropping a rotation that has begun silently deletes the days
        // somebody stood in a service and a secretary keyed in one by one. A mark at least announces
        // itself on every screen; attendance is invisible until the day it is needed. Refused for the
        // same reason and in the same words: this act may rewrite a plan, never a record of what
        // happened.
        if (ServicePeriods.Any(p => p.Attendance.Count > 0))
            return AppResult.Failure<int>(StageErrors.DeclaredRotationOverAttendance);

        int dropped = ServicePeriods.Count;

        foreach (var existing in ServicePeriods.ToList())
            ServicePeriods.Remove(existing);

        // Do NOT pre-set the Id: on an already-tracked assignment a non-sentinel store-generated key
        // makes EF classify the child Modified (UPDATE a non-existent row) instead of Added. Same
        // gotcha as Delocalize and TransferToCohort.
        foreach (var period in periods)
            ServicePeriods.Add(new ServicePeriod
            {
                InternshipAssignmentId = Id,
                ServiceId              = period.ServiceId,
                CohortSlotAssignmentId = null,
                StartDate              = period.StartDate,
                EndDate                = period.EndDate,
            });

        RecomputeFinalScore();
        RecomputeStatusFromPeriods();
        Raise(new AffectationImportedDomainEvent(Id, RegistrationId, stageId, periods.Count, dropped));
        return AppResult.Success(dropped);
    }

    /// <summary>
    /// Déplace la fenêtre d'<b>une</b> rotation encore à venir — la moitié « exécution » du
    /// déplacement d'une colonne publiée (phase 17.1).
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Cela passe par l'agrégat, et non par le contexte, pour deux raisons.</b> D'abord
    /// l'invariant : une rotation commencée, notée ou pointée ne se déplace pas, et un agrégat qui
    /// laisse son appelant garantir cela n'a pas d'invariant. Ensuite l'événement : « une rotation
    /// publiée a bougé » est un fait du dossier de l'étudiant, et l'acte qui en déplace des milliers
    /// d'un coup n'en levait aucun.</para>
    ///
    /// <para>⚠ <b>Elle ne décide pas de la fenêtre, elle l'applique.</b> Sous
    /// <c>StageRotationMode.SingleService</c> une période couvre une suite de colonnes et sa fenêtre
    /// est le min/max de celles-ci ; ce calcul appartient à <c>PublishedPeriodShifter</c>, qui voit
    /// toutes les cellules. Le refaire ici demanderait à l'agrégat de connaître la grille.</para>
    ///
    /// <para>⚠ <b>Aucun recalcul de note ni de statut.</b> Déplacer une rotation qui n'a ni commencé
    /// ni été notée ne change rien à ce que l'étudiant a obtenu — c'est précisément ce que les gardes
    /// ci-dessus garantissent — donc appeler <c>RecomputeFinalScore</c> ici serait du bruit.</para>
    ///
    /// <para>⚠ <b>C'est le <i>seul</i> déplacement d'une fenêtre publiée, depuis le 18/09/2026, et
    /// l'autre a été retiré plutôt que réparé.</b> <c>PausePeriod</c> / <c>ResumePeriod</c> faisaient
    /// la même chose par accumulation : la reprise allongeait la période de
    /// <c>date − début_de_pause</c> en <b>jours calendaires</b> — un week-end dans la fenêtre comptait
    /// comme des jours perdus — puis poussait les périodes suivantes du même delta, sans lever
    /// d'événement, sans la garde <c>Movable</c> (donc par-dessus des journées de présence), sans
    /// toucher la grille dont <c>ServiceOccupancyCalculator</c> tire l'occupation, et sans rien
    /// inscrire au registre. Rejouées, elles déplaçaient deux fois.</para>
    ///
    /// <para><b>La différence tient en une phrase : celle-ci écrit des dates <i>absolues</i>.</b>
    /// Rejouée avec la même fenêtre elle ne fait rien (le court-circuit ci-dessous), ce qui est ce qui
    /// rend une cascade rattrapable. Une fenêtre d'examens se <b>déclare</b> — <c>PromotionPause</c>,
    /// qui n'écrit aucune date et se révoque — puis les colonnes qu'elle coupe se déplacent par ici.
    /// Les deux actes sont séparés parce que la déclaration est une décision et le déplacement une
    /// conséquence ; les confondre est ce qui a coûté les quatre défauts ci-dessus.</para>
    /// </remarks>
    public AppResult Reschedule(Guid servicePeriodId, DateOnly startDate, DateOnly endDate)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == servicePeriodId);

        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(servicePeriodId));

        // ⚠ La règle est celle de ServicePeriodLifecycle.Movable, pas une copie : l'aperçu d'une pause
        // annonce combien de colonnes sont déplaçables en la posant au magasin, et une garde qui
        // diverge de ce rapport propose un geste que l'agrégat refuse ensuite.
        if (!ServicePeriodLifecycle.IsMovable(period))
            return AppResult.Failure(StageErrors.PeriodCannotBeRescheduled);

        if (endDate < startDate)
            return AppResult.Failure(StageErrors.PeriodWindowReversed(startDate, endDate));

        // Relevées avant l'écrasement : une fois la ligne écrite, les anciennes dates n'existent plus
        // nulle part, et l'événement est la moitié « dossier » de la réponse à « d'où cela vient-il ».
        var fromStart = period.StartDate;
        var fromEnd   = period.EndDate;

        // Rien à dire quand rien ne bouge : sous SingleService, déplacer une colonne du *milieu* d'un
        // séjour laisse la fenêtre du séjour inchangée, et un événement par période couverte ferait
        // lire « des milliers de rotations déplacées » là où aucune ne l'est.
        if (fromStart == startDate && fromEnd == endDate)
            return AppResult.Success();

        period.StartDate = startDate;
        period.EndDate   = endDate;

        Raise(new ServicePeriodRescheduledDomainEvent(
            Id, RegistrationId, period.Id, fromStart, fromEnd, startDate, endDate));

        return AppResult.Success();
    }

    /// <summary>
    /// Repousse la <b>fin</b> d'une rotation, en laissant son début où il est — ce qu'une rotation
    /// déjà commencée autorise et qu'un déplacement n'autorise pas.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Pourquoi un second acte plutôt qu'un paramètre de <see cref="Reschedule"/>.</b> Les
    /// deux ne posent pas la même question et ne lisent donc pas la même garde :
    /// <c>ServicePeriodLifecycle.Movable</c> refuse une rotation commencée ou pointée,
    /// <c>ServicePeriodLifecycle.Extendable</c> les accepte et refuse une rotation close, notée ou
    /// interrompue. Un drapeau sur un seul acte aurait fait porter à l'appelant le choix de la garde,
    /// c'est-à-dire aurait rendu l'invariant optionnel.</para>
    ///
    /// <para>⚠ <b>Elle ne sait que repousser, et le refus vit dans le nom.</b> Ramener la fin en
    /// arrière laisserait les journées de présence comprises entre la nouvelle fin et l'ancienne sur
    /// des dates que la fenêtre ne couvre plus — exactement le tort qu'un déplacement fait au registre.
    /// Plutôt que d'ajouter une garde sur les présences, l'acte se limite au sens où il est sûr : une
    /// fenêtre qui ne fait que croître contient toujours tout ce qu'elle contenait.</para>
    ///
    /// <para>⚠ <b>Une date absolue, jamais un delta — comme <see cref="Reschedule"/> et pour la même
    /// raison.</b> C'est ce qui sépare cet acte de la pause par étape retirée le 18/09/2026 : rejoué
    /// avec la même date il ne fait rien, donc un recalcul d'axe peut être relancé autant de fois
    /// qu'on veut sans que la rotation s'allonge à chaque passage. Une méthode <c>ExtendBy(jours)</c>
    /// aurait été l'accumulation qui a coûté le retrait.</para>
    ///
    /// <para>⚠ <b>Le même événement que le déplacement, et c'est voulu.</b> Un allongement <i>est</i>
    /// un changement de fenêtre ; <c>ServicePeriodRescheduledDomainEvent</c> transporte déjà les deux
    /// fenêtres, donc un lecteur voit que le début n'a pas bougé sans qu'un second type le lui dise.
    /// Deux événements pour un fait auraient obligé chaque futur consommateur à s'abonner aux deux.</para>
    /// </remarks>
    public AppResult ExtendTo(Guid servicePeriodId, DateOnly newEndDate)
    {
        var period = ServicePeriods.FirstOrDefault(p => p.Id == servicePeriodId);

        if (period is null)
            return AppResult.Failure(StageErrors.PeriodNotFound(servicePeriodId));

        if (!ServicePeriodLifecycle.IsExtendable(period))
            return AppResult.Failure(StageErrors.PeriodCannotBeExtended);

        if (newEndDate < period.EndDate)
            return AppResult.Failure(
                StageErrors.PeriodExtensionGoesBackwards(period.EndDate, newEndDate));

        // Rejeu : la même fin demandée deux fois n'est pas un allongement, et un événement le ferait
        // lire comme tel.
        if (newEndDate == period.EndDate)
            return AppResult.Success();

        var fromEnd = period.EndDate;
        period.EndDate = newEndDate;

        Raise(new ServicePeriodRescheduledDomainEvent(
            Id, RegistrationId, period.Id,
            period.StartDate, fromEnd,
            period.StartDate, newEndDate));

        return AppResult.Success();
    }

    /// <summary>
    /// Puts back the rotation an import replaced, exactly as it stood. The inverse of
    /// <see cref="DeclareRotation"/>, and deliberately written as its mirror.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>It restores the flags and the cell, not merely the service and the dates.</b> A
    /// période that was published carries <c>CohortSlotAssignmentId</c>; putting it back without that
    /// link would leave the promotion's plan and its execution records permanently out of agreement —
    /// the grid showing a cell nothing was published from — and no screen would say why. Same for
    /// <c>IsStarted</c> / <c>IsComplete</c>: a rotation that was under way must not come back as a
    /// plan.</para>
    ///
    /// <para>⚠ <b>Refused over a mark or over attendance</b>, like its inverse — but for a different
    /// reason. Here they cannot be the import's doing: it refused to touch them on the way in. They
    /// are something that arrived <i>since</i>, which means the affectation is no longer the one the
    /// import wrote, and walking it back would destroy work nobody recorded here.</para>
    ///
    /// <para>⚠ <b>The délocalisation child is rebuilt, not re-pointed.</b> A replaced période that was
    /// itself délocalisé carries the only trace of a stage nobody here supervised — its motif — so the
    /// restore writes it back rather than leaving a délocalisé période with no <c>Delocalization</c>
    /// behind it, which every reader would show as a blank.</para>
    /// </remarks>
    public Result<int> RestoreRotation(int stageId, IReadOnlyList<RestoredPeriod> periods)
    {
        if (ServicePeriods.Any(p => p.Evaluation is not null))
            return AppResult.Failure<int>(StageErrors.RestoredRotationOverMark);

        if (ServicePeriods.Any(p => p.Attendance.Count > 0))
            return AppResult.Failure<int>(StageErrors.RestoredRotationOverAttendance);

        int removed = ServicePeriods.Count;

        foreach (var existing in ServicePeriods.ToList())
            ServicePeriods.Remove(existing);

        // No pre-set Id — same store-generated-key gotcha as everywhere else on a tracked aggregate.
        foreach (var period in periods)
            ServicePeriods.Add(new ServicePeriod
            {
                InternshipAssignmentId = Id,
                ServiceId              = period.ServiceId,
                CohortSlotAssignmentId = period.CohortSlotAssignmentId,
                StartDate              = period.StartDate,
                EndDate                = period.EndDate,
                IsStarted              = period.IsStarted,
                IsComplete             = period.IsComplete,
                IsInterrupted          = period.IsInterrupted,
                IsPaused               = period.IsPaused,
                IsDelocalized          = period.IsDelocalized,
                Delocalization         = period.IsDelocalized
                    ? new Delocalization { Reason = period.DelocalizationReason ?? string.Empty }
                    : null,
            });

        RecomputeFinalScore();
        RecomputeStatusFromPeriods();
        Raise(new AffectationImportRolledBackDomainEvent(Id, RegistrationId, stageId, periods.Count, removed));
        return AppResult.Success(removed);
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

