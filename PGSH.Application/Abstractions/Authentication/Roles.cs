namespace PGSH.Application.Abstractions.Authentication;

/// <summary>
/// Keycloak realm role names, mirrored on the frontend in <c>common/constants/roles.ts</c>.
/// Used by handlers to grant administrative override of the per-chef data scoping.
/// </summary>
public static class Roles
{
    public const string Student = "Student";
    public const string Scolarite = "Scolarite";
    public const string Secretaire = "Secretaire";
    public const string Professor = "Professor";
    public const string Employee = "Employee";
    public const string SuperUser = "SuperUser";

    /// <summary>Roles allowed to act on any service's periods/evaluations, bypassing chef scoping.</summary>
    public static readonly string[] Administrative = [Scolarite, Secretaire, SuperUser];
}
