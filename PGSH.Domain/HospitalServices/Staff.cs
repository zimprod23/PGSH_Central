namespace PGSH.Domain.HospitalServices;

public sealed class Staff
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public Mission Mission { get; set; }
    public string? PersonalEmail { get; set; } = string.Empty;
}

public enum Mission
{
    Infermier,
    Secretaire,
    Autre
}
