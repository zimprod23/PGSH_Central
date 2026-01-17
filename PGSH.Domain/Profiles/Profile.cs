using PGSH.Domain.Users;
using PGSH.SharedKernel;

namespace PGSH.Domain.Profiles;

public class Profile
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Address? Address { get; set; }
    public string? CIN { get; set; }
    public Gender Gender { get; set; }
    public Status Status { get; set; } = new(CivilStatus.Civil, NationalityStatus.Marocaine);
    public DateOnly? DateOfBirth { get; set; }
    public string? PlaceOfBirth  { get; set; }
}
