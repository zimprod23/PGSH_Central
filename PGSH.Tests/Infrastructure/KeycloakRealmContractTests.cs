using System.Text.Json;
using FluentAssertions;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Tests.Postgres;
using Xunit;

namespace PGSH.Tests.Infrastructure;

/// <summary>
/// Boots <c>keycloak/pgsh-realm.json</c> in a real Keycloak and asks it for a real token, then checks
/// the token against what the application actually requires of it.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Why this exists, in one sentence: three defects shipped in that file in a single day,
/// and every one of them was found by a human watching an exception.</b></para>
///
/// <list type="number">
///   <item><c>"_comment"</c> at the top — Keycloak refuses unknown properties, the import aborted and
///   the <b>container exited</b>. No identity provider at all.</item>
///   <item>no <c>default-roles-pgsh</c> on the users — no <c>account</c> client roles, so <b>no
///   <c>aud</c> claim</b>; the API validates the audience <c>account</c>, answered 401, and the client
///   turns a 401 into a logout. Sign in, bounce out, repeat.</item>
///   <item><c>defaultClientScopes</c> written out by hand without <c>basic</c> — the scope carrying
///   <c>sub</c> in Keycloak 25 — so tokens were valid, signed, and <b>named nobody</b>.</item>
/// </list>
///
/// <para>⚠ <b>The structural tests could not have caught any of them.</b>
/// <see cref="KeycloakRealmFileTests"/> reads the file's shape; these three are facts about what
/// Keycloak <i>emits</i>, which only Keycloak can answer. Same split as the rest of this suite — the
/// in-memory provider versus a real PostgreSQL — and for the same reason: a green suite that never
/// asked the server proves nothing about the server.</para>
///
/// <para>⚠ <b>It asserts the contract, not the configuration.</b> Nothing here says « the client must
/// carry scope X ». It says the token must carry a subject, an audience, an e-mail and the roles —
/// what <c>ClaimsPrincipalExtensions.GetUserId</c>, <c>Keycloak:Audience</c>,
/// <c>UserContext.SyncAsync</c> and <c>KeycloakRoleTransformer</c> actually read. Keycloak may move
/// which scope produces <c>sub</c> in a future version and these cases stay right.</para>
/// </remarks>
public sealed class KeycloakRealmContractTests(KeycloakRealmFixture keycloak)
    : IClassFixture<KeycloakRealmFixture>
{
    /// <summary>The audience <c>PGSH.API</c> validates — <c>Program.cs</c>, <c>options.Audience</c>.</summary>
    private const string RequiredAudience = "account";

    private async Task<JsonElement> TokenFor(string username)
    {
        var (ok, failure, payload) = await keycloak.TokenFor(username);
        ok.Should().BeTrue(failure);
        return payload;
    }

    /// <summary>
    /// ⚠ <b>Defect 3.</b> <c>GetUserId</c> needs a subject; with <c>basic</c> missing the token had
    /// none — valid, signed, naming nobody, which the application could only report as
    /// « User id is unavailable ».
    /// </summary>
    [PostgresFact]
    public async Task The_token_carries_a_subject()
    {
        var token = await TokenFor("admin.pgsh@um5.ac.ma");

        token.TryGetProperty("sub", out var sub).Should().BeTrue(
            "without `sub` there is nobody to be: GetUserId has nothing to read and every request fails");

        Guid.TryParse(sub.GetString(), out _).Should().BeTrue("the subject is read as a Guid");
    }

    /// <summary>
    /// ⚠ <b>Defect 2.</b> No audience means 401 on every call, and <c>errorMiddleware</c> turns a 401
    /// into <c>keycloak.logout()</c> — a login loop with no explanation anywhere.
    /// </summary>
    [PostgresFact]
    public async Task The_token_carries_the_audience_the_api_validates()
    {
        var token = await TokenFor("admin.pgsh@um5.ac.ma");

        token.TryGetProperty("aud", out var aud).Should().BeTrue(
            "the API validates an audience; a token without one is refused on every request");

        var audiences = aud.ValueKind == JsonValueKind.Array
            ? aud.EnumerateArray().Select(a => a.GetString()!)
            : [aud.GetString()!];

        audiences.Should().Contain(RequiredAudience);
    }

    /// <summary>
    /// <c>UserContext.SyncAsync</c> falls back to the e-mail to link a Keycloak account to its local
    /// <c>User</c> row — the path that re-attaches a <b>rebuilt realm</b> to an existing database,
    /// which is exactly what a restored backup needs. Without the claim, that fallback cannot run.
    /// </summary>
    [PostgresFact]
    public async Task The_token_carries_the_email_the_fallback_links_on()
    {
        var token = await TokenFor("admin.pgsh@um5.ac.ma");

        token.GetProperty("email").GetString().Should().Be("admin.pgsh@um5.ac.ma");
    }

    /// <summary>
    /// The roles arrive where <c>KeycloakRoleTransformer</c> looks — <c>realm_access.roles</c>.
    /// </summary>
    [PostgresFact]
    public async Task The_roles_arrive_where_the_transformer_reads_them()
    {
        var token = await TokenFor("admin.pgsh@um5.ac.ma");

        var roles = token.GetProperty("realm_access").GetProperty("roles")
            .EnumerateArray().Select(r => r.GetString()!).ToList();

        roles.Should().Contain(Roles.Scolarite);
        roles.Should().Contain(Roles.SuperUser);
    }

    /// <summary>
    /// ⚠ <b>Every account, not merely the admin.</b> Roles are per user and so is the default-roles
    /// composite: checking one account would pass while six others were unusable — which is precisely
    /// the state the realm shipped in.
    /// </summary>
    [PostgresTheory]
    [InlineData("admin.pgsh@um5.ac.ma", Roles.Scolarite)]
    [InlineData("employee.test@um5.ac.ma", Roles.Professor)]
    [InlineData("chef.cardio@um5.ac.ma", Roles.Professor)]
    [InlineData("secretaire.test@um5.ac.ma", Roles.Secretaire)]
    [InlineData("amine.bennani@um5.ac.ma", Roles.Student)]
    [InlineData("etudiant.test2@um5.ac.ma", Roles.Student)]
    [InlineData("etudiant.test3@um5.ac.ma", Roles.Student)]
    public async Task Every_account_receives_a_usable_token(string username, string expectedRole)
    {
        var token = await TokenFor(username);

        token.TryGetProperty("sub", out _).Should().BeTrue("no subject means no user");
        token.TryGetProperty("aud", out _).Should().BeTrue("no audience means 401 then logout");
        token.GetProperty("email").GetString().Should().Be(username);

        token.GetProperty("realm_access").GetProperty("roles")
            .EnumerateArray().Select(r => r.GetString()!)
            .Should().Contain(expectedRole);
    }
}
