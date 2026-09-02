using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

public sealed class Registration : Entity
{
    public Guid Id { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; }
    public RegistrationStatus Status { get; set; } = RegistrationStatus.Pending;
    public int LevelId { get; set; }
    public Level Level { get; set; }
    public Student Student { get; set; }
    public int? AcademicGroupId { get; set; } // The Group assigned for THIS year
    public AcademicGroup? AcademicGroup { get; set; }
    public Guid StudentId { get; set; }
    public FailureReasons? failureReasons { get; set; }
    public DateTime? RegistrationDate { get; set; }

    /// <summary>
    /// Whether <see cref="Status"/> is a verdict the faculty declared or one PGSH deduced. Null while
    /// the year is still running — and null on every year imported from the legacy base, which is why
    /// they all still read "en cours".
    /// </summary>
    public RegistrationOutcomeSource? OutcomeSource { get; private set; }

    /// <summary>When the verdict was recorded in PGSH — not when the jury sat.</summary>
    public DateTime? OutcomeRecordedOn { get; private set; }

    /// <summary>
    /// The CNPN that governed this student at this level in this year — resolved once, when the
    /// registration was created, and never recomputed.
    ///
    /// <para><b>This is what a student owes, and <c>Student.CnpnVersionId</c> is not.</b> The student
    /// carries one stamp for "the text he is on now"; a level's requirement set has to be read as of
    /// the year he sat that level. A 4ᵉ année student still owing two stages from his 3ᵉ année owes
    /// them under the 3ᵉ année of <i>his</i> 3ᵉ année — reshaping that level for the promotions that
    /// followed must not reach back and change his debt. Only a stamp on the registration can say
    /// that, because only the registration is fixed to a (level, year).</para>
    ///
    /// <para>Nullable, and it will stay that way. The six imported years were backfilled from the
    /// student's stamp where he had one, and ~2,200 enrolled students have none at all: null means
    /// "never resolved", and every reader falls back to <c>Student.CnpnVersionId</c> rather than
    /// treating it as "owes nothing".</para>
    /// </summary>
    public int? CnpnVersionId { get; private set; }
    public CnpnVersion? CnpnVersion { get; private set; }

    /// <summary>How <see cref="CnpnVersionId"/> was decided. Null exactly when the stamp is null.</summary>
    public RegistrationCnpnSource? CnpnSource { get; private set; }

    public ICollection<InternshipAssignment> InternshipAssignments { get; set; } = new List<InternshipAssignment>();

    /// <summary>
    /// Signalements posés sur cette inscription. Kept after release rather than deleted, so the file
    /// can still say the student was flagged, on what evidence, and who cleared him.
    /// </summary>
    public ICollection<RegistrationHold> Holds { get; set; } = new List<RegistrationHold>();

    /// <summary>
    /// Whether planning must leave this registration alone. ⚠ Reads <see cref="Holds"/>, so the
    /// collection has to be loaded; in a query use <see cref="RegistrationHoldPolicy"/>'s expression
    /// instead, which is the same rule in the form the provider can translate.
    /// </summary>
    public bool IsOnHold =>
        Holds.Any(h => h.ReleasedOn is null && h.Reason.BlocksPlanning());

    /// <summary>
    /// Whether anything at all is flagged on this registration — blocking or merely advisory. What
    /// the worklist counts; <see cref="IsOnHold"/> is what planning obeys.
    /// </summary>
    public bool IsFlagged => Holds.Any(h => h.ReleasedOn is null);

    /// <summary>
    /// Records the governing CNPN. The only writer of <see cref="CnpnVersionId"/>.
    /// </summary>
    /// <remarks>
    /// <para>Called once, by <c>RegistrationCnpnStamper</c>, as the registration is created. Re-stamping an
    /// existing registration is a separate administrative act — applying a rule authored after the
    /// réinscription had already run — and it is refused here once the year has been pronounced:
    /// a closed year's requirement set is the record of what the student was judged against, and a
    /// verdict whose obligations moved afterwards is not readable.</para>
    /// </remarks>
    public Result StampCnpnVersion(int cnpnVersionId, RegistrationCnpnSource source)
    {
        if (CnpnVersionId == cnpnVersionId && CnpnSource == source)
            return Result.Success();

        if (CnpnVersionId is not null && OutcomeSource is not null)
            return Result.Failure(RegistrationErrors.CnpnFrozenByOutcome(Id));

        int? previous = CnpnVersionId;

        CnpnVersionId = cnpnVersionId;
        CnpnSource = source;

        if (previous != cnpnVersionId)
            Raise(new RegistrationCnpnStampedDomainEvent(
                Id, StudentId, AcademicYearId, LevelId, previous, cnpnVersionId, source));

        return Result.Success();
    }

    public void TransferToGroup(int newGroupId, string? reason)
    {
        int? previousGroupId = AcademicGroupId;
        AcademicGroupId = newGroupId;
        Raise(new StudentGroupTransferredDomainEvent(Id, StudentId, previousGroupId, newGroupId, reason));
    }

    /// <summary>
    /// Closes the academic year with the verdict pronounced in deliberation. The only writer of
    /// <see cref="OutcomeSource"/> and <see cref="OutcomeRecordedOn"/>.
    /// </summary>
    /// <remarks>
    /// Re-declaring is allowed — a jury corrects itself and the corrected file is uploaded again —
    /// but an <see cref="RegistrationOutcomeSource.Inferred"/> verdict may never overwrite a
    /// <see cref="RegistrationOutcomeSource.Declared"/> one. PGSH's reading of an enrolment sequence
    /// is a guess; the faculty's file is a fact, and a guess that can silently replace a fact makes
    /// the whole column unreadable.
    /// </remarks>
    public Result RecordYearOutcome(
        RegistrationStatus outcome,
        RegistrationOutcomeSource source,
        FailureReasons? motif,
        DateTime recordedOn)
    {
        if (!outcome.IsYearOutcome())
            return Result.Failure(RegistrationErrors.NotAYearOutcome(outcome));

        if (OutcomeSource == RegistrationOutcomeSource.Declared && source == RegistrationOutcomeSource.Inferred)
            return Result.Failure(RegistrationErrors.OutcomeAlreadyDeclared(Id));

        RegistrationStatus previous = Status;

        Status = outcome;
        OutcomeSource = source;
        OutcomeRecordedOn = recordedOn;
        failureReasons = motif;

        Raise(new RegistrationYearOutcomeRecordedDomainEvent(
            Id, StudentId, AcademicYearId, previous, outcome, source));

        return Result.Success();
    }

    /// <summary>
    /// Withdraws a verdict and puts the year back in progress.
    /// </summary>
    /// <remarks>
    /// <para>The counterpart of <see cref="RecordYearOutcome"/>, and it has to exist: a promotion closed
    /// on the wrong file, or closed before the jury had finished, cannot be corrected by recording a
    /// different verdict — <c>Active</c> is not a verdict, so <c>RecordYearOutcome</c> refuses it, and
    /// the réinscription would otherwise carry the mistake into the following year.</para>
    ///
    /// <para>⚠ It does <b>not</b> undo what the verdict caused. A réinscription already run has created
    /// next year's registration, and that row is not touched here — deleting it would take its groups,
    /// cohorts and périodes with it. Re-opening a year is a correction to <em>this</em> registration;
    /// the caller is told to look at the next one.</para>
    /// </remarks>
    public Result ReopenYear(string? reason)
    {
        if (OutcomeSource is null)
            return Result.Failure(RegistrationErrors.NoOutcomeToReopen);

        RegistrationStatus previous = Status;

        Status = RegistrationStatus.Active;
        OutcomeSource = null;
        OutcomeRecordedOn = null;
        failureReasons = null;

        Raise(new RegistrationYearReopenedDomainEvent(Id, StudentId, AcademicYearId, previous, reason));

        return Result.Success();
    }

    /// <summary>
    /// Withdraws this registration from planning until somebody settles <paramref name="evidence"/>.
    /// The only writer of <see cref="Holds"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Idempotent per reason.</b> A hold already standing for the same
    /// <paramref name="reason"/> is left exactly as it was — its evidence is the snapshot taken the
    /// first time, and re-running the réinscription roll (which is designed to be re-runnable) must
    /// not stack four identical flags on one student or quietly rewrite the sentence somebody is
    /// about to act on. Two <em>different</em> reasons legitimately coexist.</para>
    ///
    /// <para>⚠ It changes no status and annuls nothing. A held registration is still
    /// <c>Active</c>, still carries whatever verdict was pronounced on it, and keeps every période
    /// already published under it — removing those is
    /// <c>UnpublishCohortScheduleCommand</c>'s act, which names what it costs and asks twice.</para>
    /// </remarks>
    public Result<RegistrationHold> PlaceOnHold(
        RegistrationHoldReason reason,
        string evidence,
        DateTime raisedOn,
        Guid? raisedByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            return Result.Failure<RegistrationHold>(RegistrationErrors.HoldEvidenceRequired);

        var standing = Holds.FirstOrDefault(h => h.ReleasedOn is null && h.Reason == reason);
        if (standing is not null)
            return standing;

        var hold = new RegistrationHold
        {
            // ⚠ The key is left to the store. Assigning one to a child added to an already-tracked
            // parent makes EF classify it Modified rather than Added — UPDATE … WHERE Id = <new
            // guid>, nought rows, DbUpdateConcurrencyException. See InternshipAssignment.Delocalize.
            RegistrationId = Id,
            Reason = reason,
            Evidence = evidence.Trim(),
            RaisedOn = raisedOn,
            RaisedByUserId = raisedByUserId,
        };

        Holds.Add(hold);

        Raise(new RegistrationHeldDomainEvent(Id, StudentId, AcademicYearId, reason, hold.Evidence));

        return hold;
    }

    /// <summary>
    /// Clears one hold, so the registration takes part in planning again once nothing else holds it.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Nothing lifts a hold but this.</b> The condition that raised it ceasing to be true does
    /// not — a registration that slipped back into the répartition the day an évaluation was keyed
    /// in would be exactly the silent behaviour the flag exists to remove. The row survives its own
    /// release, carrying both the evidence and the note, because « qui l'a débloqué et sur quoi » is
    /// the half of the record an audit actually asks for.
    /// </remarks>
    public Result ReleaseHold(
        Guid holdId,
        string releaseNote,
        DateTime releasedOn,
        Guid? releasedByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(releaseNote))
            return Result.Failure(RegistrationErrors.HoldReleaseNoteRequired);

        // ⚠ The key is store-generated, so every hold not yet saved carries Guid.Empty — and matching
        // on it would release whichever unsaved flag happens to sit first in the collection, silently
        // lifting a different one from the one asked for. Nothing on the worklist can carry an empty
        // id, so this is only reachable from code that has not saved yet, and it is a defect there.
        if (holdId == Guid.Empty)
            return Result.Failure(RegistrationErrors.HoldNotFound(holdId));

        var hold = Holds.FirstOrDefault(h => h.Id == holdId);
        if (hold is null)
            return Result.Failure(RegistrationErrors.HoldNotFound(holdId));

        if (hold.ReleasedOn is not null)
            return Result.Failure(RegistrationErrors.HoldAlreadyReleased(holdId));

        hold.ReleasedOn = releasedOn;
        hold.ReleasedByUserId = releasedByUserId;
        hold.ReleaseNote = releaseNote.Trim();

        Raise(new RegistrationHoldReleasedDomainEvent(
            Id, StudentId, holdId, hold.Reason, hold.ReleaseNote));

        return Result.Success();
    }
}
