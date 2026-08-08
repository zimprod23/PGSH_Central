using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Users;
using PGSH.SharedKernel;

namespace PGSH.Domain.Students;

public sealed class Student: User
{
    //public Guid Id { get; set; }
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

    public ICollection<Registration> registrations { get; set; } = new List<Registration>();
    public ICollection<History> history { get; set; } = new List<History>();
    public Academy? Academy { get; set; }
    public Province? Province { get; set; }
    public int? Ranking {  get; set; }

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

    public Result AddRegistration(Registration registration)
    {
        // Check for duplicate registrations by Year ID instead of DateOnly
        if (registrations.Any(r => r.AcademicYearId == registration.AcademicYearId))
        {
            // Note: You may want to update RegistrationErrors to accept the Year ID or Label
            return Result.Failure(RegistrationErrors.DuplicateRegistration(this.Id, registration.AcademicYearId));
        }

        registrations.Add(registration);

        // Event now carries the ID of the Year Entity
        registration.Raise(new StudentRegisteredDomainEvent(
            registration.Id,
            this.Id,
            registration.LevelId,
            registration.AcademicYearId));

        return Result.Success();
    }

    public Result UpdateRegistration(
        Guid registrationId,
        RegistrationStatus status,
        int academicYearId,
        int levelId,
        FailureReasons? failure)
    {
        var registration = registrations.FirstOrDefault(r => r.Id == registrationId);
        if (registration is null) return Result.Failure(RegistrationErrors.NotFound(registrationId));

        // Validation: If year is changing, ensure student isn't already registered for that Year ID
        if (registration.AcademicYearId != academicYearId &&
            registrations.Any(r => r.AcademicYearId == academicYearId))
        {
            return Result.Failure(RegistrationErrors.DuplicateRegistration(this.Id, academicYearId));
        }

        // Update properties
        registration.Status = status;
        registration.AcademicYearId = academicYearId;
        registration.LevelId = levelId;
        registration.failureReasons = failure;

        registration.Raise(new RegistrationUpdatedDomainEvent(registration.Id, status));

        return Result.Success();
    }

    public Result RemoveRegistration(Guid registrationId)
    {
        var registration = registrations.FirstOrDefault(r => r.Id == registrationId);

        if (registration is null)
            return Result.Failure(RegistrationErrors.NotFound(registrationId));

        // Optional: Add business rules, e.g., "Cannot delete a validated registration"
        if (registration.Status == RegistrationStatus.Validated)
            return Result.Failure(RegistrationErrors.Conflict("Delete",registrationId));

        registrations.Remove(registration);

        return Result.Success();
    }
}
