using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Domain.Users;

namespace PGSH.Application.Students.GetById;

public sealed record StudentResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string? UserName { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public Address? Address { get; set; }
    public string? CIN { get; set; }
    public Gender Gender { get; set; }
    public Status Status { get; set; } = new(CivilStatus.Civil, NationalityStatus.Marocaine);
    public DateOnly? DateOfBirth { get; set; }
    public string? PlaceOfBirth { get; set; }
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
}
