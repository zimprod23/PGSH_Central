using PGSH.SharedKernel;

namespace PGSH.Domain.Users;

public  class User : Entity
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string? UserName { get; set; }
    
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    //public Profile? Profile { get; set; }

    public Address? Address { get; set; }
    public string? CIN { get; set; }
    public Gender Gender { get; set; }
    public Status Status { get; set; } = new(CivilStatus.Civil, NationalityStatus.Marocaine);
    public DateOnly? DateOfBirth { get; set; }
    public string? PlaceOfBirth { get; set; }
    public string? IdentityProviderId { get; private set; }

    public void LinkIdentity(string providerId)
    {
        if (IdentityProviderId != null)
            throw new InvalidOperationException("User already linked");

        IdentityProviderId = providerId;
        //IdentityLinkedAt = DateTime.UtcNow;
    }


}
