using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Users;
using PGSH.SharedKernel;

namespace PGSH.Domain.Students;

public sealed class Student : User
{
    public AcademicProgram AcademicProgram { get; set; }
    public string CNE { get; set; }
    public decimal AccessGrade { get; set; } = 10.01M;
    public string Appogee { get; set; }
    public BacSeries BacSeries { get; set; }
    public AgreementType AgreementType { get; set; } = AgreementType.None;
    public string BacYear { get; set; }
    /// <summary>
    /// The CNPN this student is governed by, fixed at entry and carried to graduation.
    ///
    /// <para>It lives on the student rather than on each <see cref="Registration"/> precisely because
    /// it must not move: arrêté 1650.25 art. 2 keeps everyone registered before 2024-2025 under the
    /// previous text however long they take, so a per-year copy would only be an opportunity to
    /// drift. Null means not yet assigned — see <c>CnpnAssignment</c>.</para>
    ///
    /// <para>Changing it changes how many years the student owes, so it is not ordinary edit
    /// surface: <see cref="AssignCnpnVersion"/> is the only writer, and reassignment is an explicit
    /// administrative act.</para>
    /// </summary>
    public int? CnpnVersionId { get; private set; }
    public CnpnVersion? CnpnVersion { get; private set; }

    /// <summary>
    /// True when <see cref="CnpnVersionId"/> was inferred rather than read from a recorded entry —
    /// the student's earliest registration is missing, so entry was deduced from the level they sit
    /// in now. Surfaced so scolarité can confirm; the assignment is usable meanwhile.
    /// </summary>
    public bool CnpnAssignmentIsInferred { get; private set; }

    /// <summary>
    /// Every year this student has been enrolled, across levels and programmes — 2,635 students in
    /// the imported history have repeated a level, so this is routinely more than one row per level.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>A registration is created through the paths that stamp it</b> (inscription,
    /// réinscription, the registration form), never by pushing onto this collection: it has to be
    /// given its governing CNPN as it is created, which is <c>RegistrationCnpnStamper</c>'s job.
    /// The setter is closed for that reason, and because nothing ever assigned it — EF populates it
    /// through the backing field (<c>PropertyAccessMode.Field</c>, set in <c>UserConfiguration</c>).
    /// </remarks>
    public ICollection<Registration> Registrations { get; private set; } = new List<Registration>();

    /// <summary>
    /// The student's recorded lifecycle events — inscription, validation, transfert, délocalisation.
    /// </summary>
    /// <remarks>
    /// Named <c>HistoryEntries</c> rather than <c>History</c>: a property carrying its own element
    /// type's name compiles, but it shadows the type inside this class, so the first person who needs
    /// to write <c>History</c> as a type here gets an error with no obvious cause.
    /// </remarks>
    public ICollection<History> HistoryEntries { get; private set; } = new List<History>();
    public Academy? Academy { get; set; }
    public Province? Province { get; set; }
    public int? Ranking { get; set; }

    /// <summary>
    /// Stamps the governing CNPN. Idempotent for the same version, and a no-op once a confirmed
    /// assignment exists — re-running the backfill must never quietly move a student between texts.
    /// An inferred assignment may be upgraded to a confirmed one, and
    /// <paramref name="overrideExisting"/> covers the deliberate administrative correction.
    /// </summary>
    public Result AssignCnpnVersion(int cnpnVersionId, bool isInferred, bool overrideExisting = false)
    {
        bool alreadySettled = CnpnVersionId is not null && !CnpnAssignmentIsInferred;

        if (alreadySettled && CnpnVersionId != cnpnVersionId && !overrideExisting)
            return Result.Failure(StudentErrors.CnpnAlreadyAssigned(Id, CnpnVersionId!.Value));

        // A confirmed reading never regresses to an inferred one for the same version.
        if (CnpnVersionId == cnpnVersionId && !CnpnAssignmentIsInferred)
            return Result.Success();

        int? previous = CnpnVersionId;
        CnpnVersionId = cnpnVersionId;
        CnpnAssignmentIsInferred = isInferred;

        if (previous != cnpnVersionId)
            Raise(new StudentCnpnVersionAssignedDomainEvent(Id, previous, cnpnVersionId, isInferred));

        return Result.Success();
    }

    /// <summary>
    /// Removes the stamp, for the one case in which keeping it would assert something false: the
    /// student has moved to another programme and no text of that programme could be resolved for
    /// him. A <c>CnpnVersion</c> belongs to exactly one <c>AcademicProgram</c>, so the stamp he
    /// arrived with names a cursus he has left.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>This is not an undo for <see cref="AssignCnpnVersion"/>.</b> It is deliberately not
    /// reachable as an ordinary edit: the only caller is the réorientation path, which has just
    /// established that the old text no longer applies. Null here means « never resolved » — the same
    /// thing it means on the ~2 200 students nobody has stamped — and every reader already falls back
    /// on it gracefully, which is what makes stating less the honest answer rather than a loss.
    /// </remarks>
    public void ClearCnpnVersion()
    {
        if (CnpnVersionId is not { } previous) return;

        CnpnVersionId = null;
        CnpnAssignmentIsInferred = false;

        Raise(new StudentCnpnVersionClearedDomainEvent(Id, previous));
    }

    public Result AddRegistration(Registration registration)
    {
        // Check for duplicate registrations by Year ID instead of DateOnly
        if (Registrations.Any(r => r.AcademicYearId == registration.AcademicYearId))
        {
            // Note: You may want to update RegistrationErrors to accept the Year ID or Label
            return Result.Failure(RegistrationErrors.DuplicateRegistration(this.Id, registration.AcademicYearId));
        }

        Registrations.Add(registration);

        // Event now carries the ID of the Year Entity
        registration.Raise(new StudentRegisteredDomainEvent(
            registration.Id,
            this.Id,
            registration.LevelId,
            registration.AcademicYearId));

        return Result.Success();
    }

    /// <summary>
    /// Edits a registration in place.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><paramref name="status"/> goes through <see cref="Registration.RecordYearOutcome"/> and
    /// <see cref="Registration.ReopenYear"/>, never through the setter.</b> It used to be a plain
    /// assignment, which meant the edit form could write « Admis » onto a registration while leaving
    /// <c>OutcomeSource</c> null — a verdict nobody declared. The réinscription then reported that
    /// student as « aucune décision enregistrée » and refused to carry him over, with an edit screen
    /// showing the verdict in place: the field said one thing and the rollover another, and neither
    /// was wrong about what it read.
    /// </remarks>
    public Result UpdateRegistration(
        Guid registrationId,
        RegistrationStatus status,
        int academicYearId,
        int levelId,
        FailureReasons? failure,
        DateTime recordedOn)
    {
        var registration = Registrations.FirstOrDefault(r => r.Id == registrationId);
        if (registration is null) return Result.Failure(RegistrationErrors.NotFound(registrationId));

        // Validation: If year is changing, ensure student isn't already registered for that Year ID
        if (registration.AcademicYearId != academicYearId &&
            Registrations.Any(r => r.AcademicYearId == academicYearId))
        {
            return Result.Failure(RegistrationErrors.DuplicateRegistration(this.Id, academicYearId));
        }

        registration.AcademicYearId = academicYearId;
        registration.LevelId = levelId;

        if (status.IsYearOutcome())
        {
            var outcome = registration.RecordYearOutcome(
                status, RegistrationOutcomeSource.Declared, failure, recordedOn);

            if (outcome.IsFailure) return outcome;
        }
        else
        {
            // Back to a year in progress. Where a verdict stands, that is a withdrawal and has to be
            // recorded as one; where none does, it is an ordinary edit of a row nobody has closed.
            if (registration.OutcomeSource is not null)
            {
                var reopened = registration.ReopenYear(null);
                if (reopened.IsFailure) return reopened;
            }

            registration.Status = status;
            registration.failureReasons = failure;
        }

        registration.Raise(new RegistrationUpdatedDomainEvent(registration.Id, status));

        return Result.Success();
    }

    public Result RemoveRegistration(Guid registrationId)
    {
        var registration = Registrations.FirstOrDefault(r => r.Id == registrationId);

        if (registration is null)
            return Result.Failure(RegistrationErrors.NotFound(registrationId));

        // Optional: Add business rules, e.g., "Cannot delete a validated registration"
        if (registration.Status == RegistrationStatus.Validated)
            return Result.Failure(RegistrationErrors.Conflict("Delete",registrationId));

        Registrations.Remove(registration);

        return Result.Success();
    }
}
