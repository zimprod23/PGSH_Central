using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PGSH.Tests.Integration;

/// <summary>
/// Stands in for Keycloak so the pipeline can be exercised without one.
///
/// <para>It authenticates from request headers rather than from a token: <c>X-Test-User</c> carries
/// the identity provider id (Keycloak's <c>sub</c>, which <c>UserContext</c> reads from
/// <see cref="ClaimTypes.NameIdentifier"/>) and <c>X-Test-Roles</c> a comma-separated realm role
/// list. Sending neither leaves the request anonymous, which is what makes the 401 path reachable —
/// a test auth handler that always succeeds cannot tell "allowed" from "not checked".</para>
/// </summary>
/// <remarks>
/// The roles are emitted the way Keycloak emits them — a <c>realm_access</c> JSON claim — rather than
/// as ready-made <see cref="ClaimTypes.Role"/> claims, so <c>KeycloakRoleTransformer</c> is exercised
/// too. Handing the pipeline claims it never has to transform would test the wrong shape of principal.
/// </remarks>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "IntegrationTest";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrWhiteSpace(user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ToString()),
            new(ClaimTypes.Email, $"{user}@integration.test"),
        };

        if (Request.Headers.TryGetValue(RolesHeader, out var roles) && !string.IsNullOrWhiteSpace(roles))
        {
            var names = roles.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => $"\"{r}\"");
            claims.Add(new Claim("realm_access", $$"""{"roles":[{{string.Join(',', names)}}]}"""));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}
