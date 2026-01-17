namespace PGSH.Domain.Users;

public sealed record Status(CivilStatus CivilStatus, NationalityStatus NationalityStatus);

public enum CivilStatus
{
    Militaire,
    Civil
}
public enum NationalityStatus
{
    Marocaine,
    Etrangaire
}
