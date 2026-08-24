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
    /// Records the governing CNPN. The only writer of <see cref="CnpnVersionId"/>.
    /// </summary>
    /// <remarks>
    /// <para>Called once, by <c>CnpnResolver</c>, as the registration is created. Re-stamping an
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
}
