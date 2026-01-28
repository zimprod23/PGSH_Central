using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
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
    public ICollection<Registration> registrations { get; set; } = new List<Registration>();
    public ICollection<History> history { get; set; } = new List<History>();
    public Academy? Academy { get; set; }
    public Province? Province { get; set; }
    public int? Ranking {  get; set; }

    public Result AddRegistration(Registration registration)
    {
        if (registrations.Any(r => r.AcademicYear == registration.AcademicYear))
        {
            return Result.Failure(RegistrationErrors.DuplicateRegistration(this.Id, registration.AcademicYear));
        }
        registrations.Add(registration);

        registration.Raise(new StudentRegisteredDomainEvent(registration.Id, this.Id, registration.LevelId, registration.AcademicYear));

        return Result.Success();
    }

    public Result UpdateRegistration(
        Guid registrationId,
        string status,
        DateOnly year,
        int levelId,
        FailureReasons? failure)
    {
        var registration = registrations.FirstOrDefault(r => r.Id == registrationId);
        if (registration is null) return Result.Failure(RegistrationErrors.NotFound(registrationId));

        // If year is changing, check for duplicates again
        if (registration.AcademicYear != year && registrations.Any(r => r.AcademicYear == year))
        {
            return Result.Failure(RegistrationErrors.DuplicateRegistration(this.Id, year));
        }

        // Update properties
        registration.Status = status;
        registration.AcademicYear = year;
        registration.LevelId = levelId;
        registration.failureReasons = failure;

        // Raise an event if status changes to "Validated" or "Failed"
        registration.Raise(new RegistrationUpdatedDomainEvent(registration.Id, status));

        return Result.Success();
    }

    public Result RemoveRegistration(Guid registrationId)
    {
        var registration = registrations.FirstOrDefault(r => r.Id == registrationId);

        if (registration is null)
            return Result.Failure(RegistrationErrors.NotFound(registrationId));

        // Optional: Add business rules, e.g., "Cannot delete a validated registration"
        if (registration.Status == "Validated")
            return Result.Failure(RegistrationErrors.Conflict("Delete",registrationId));

        registrations.Remove(registration);

        return Result.Success();
    }
}
