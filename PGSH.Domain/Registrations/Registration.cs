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

    public ICollection<InternshipAssignment> InternshipAssignments { get; set; } = new List<InternshipAssignment>();

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
}
