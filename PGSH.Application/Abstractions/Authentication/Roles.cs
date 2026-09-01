using PGSH.Application.Abstractions.Authorization;
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

    /// <summary>
    /// Roles allowed to act on <em>any</em> service's periods/evaluations/attendance, bypassing the
    /// per-service scoping. <see cref="Secretaire"/> is intentionally excluded: a secretary is a
    /// service-scoped operational role (presence only, and only for services she is staff of),
    /// resolved through <c>ExecutionAuthorizer.MyServiceIdsAsync</c> — not a global override.
    /// </summary>
    public static readonly string[] Administrative = [Scolarite, SuperUser];
}
