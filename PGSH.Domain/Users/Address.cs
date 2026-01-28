namespace PGSH.Domain.Users;

public record Address(string FullAddress, string? City, string? Street, string? ZIP, string? HouseNumber, string? Country)
{
    public Address(string fullAddress)
       : this(fullAddress, null, null, null, null, null)
    {
    }
    public static implicit operator Address(string? Address)
    {
        if (string.IsNullOrEmpty(Address)) return null;
        return new Address(Address);
    }
}
