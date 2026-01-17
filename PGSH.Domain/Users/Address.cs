namespace PGSH.Domain.Users;

public record Address(string FullAddress, string? City, string? Street, string? ZIP, string? HouseNumber, string? Country)
{
    public static implicit operator Address(string? Address)
    {
        if (string.IsNullOrEmpty(Address)) return null;
        return new Address(Address);
    }
}
