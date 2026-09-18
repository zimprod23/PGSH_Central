using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using PGSH.API.Extensions;
using PGSH.Application.Abstractions.Authentication;
using Xunit;

namespace PGSH.Tests.Infrastructure;

/// <summary>
/// <c>keycloak/pgsh-realm.json</c> — the realm as a versioned file, checked for the two ways it can
/// be wrong without anybody noticing until a container refuses to start or a login returns 403.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Why this file is tested at all.</b> It is not compiled, not referenced, and not linted;
/// nothing in the build reads it. The first version shipped with a <c>"_comment"</c> key at the top —
/// the obvious way to leave a note in JSON — and Keycloak's <c>RealmRepresentation</c> does not ignore
/// unknown fields: <i>« Unrecognized field "_comment" … not marked as ignorable »</i>, the import
/// aborted, and the container **exited**. A realm that takes the identity provider down on start is
/// worse than no realm file, and nothing would have caught it before a human watched the logs.</para>
///
/// <para>⚠ <b>These assertions are structural, not a schema.</b> Reproducing Keycloak's 143 known
/// properties here would be a second schema to keep in step with the first. What is pinned is the
/// class of mistake actually made — a key Keycloak cannot know — plus the couplings this repository
/// owns: the role names, and the fact that every account needs a <c>User</c> row.</para>
/// </remarks>
public class KeycloakRealmFileTests
{
    /// <summary>
    /// The repository root, walked up from <b>this source file</b> rather than from
    /// <c>AppContext.BaseDirectory</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ The output directory is not under the repository whenever the suite is built with
    /// <c>-p:BaseOutputPath=</c> — which is the documented way to run these tests while the Aspire
    /// stack holds <c>PGSH.API</c>'s <c>bin</c>. Walking up from the binary therefore finds no
    /// <c>PGSH.sln</c> and every case here fails for a reason that has nothing to do with the realm.
    /// <see cref="CallerFilePathAttribute"/> is baked in at compile time and does not move.
    /// </remarks>
    private static string RepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PGSH.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the tests must be able to find the repository root");
        return directory!.FullName;
    }

    private static string RealmPath() => Path.Combine(RepositoryRoot(), "keycloak", "pgsh-realm.json");

    private static JsonElement Realm()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RealmPath()));
        return document.RootElement.Clone();
    }

    [Fact]
    public void The_realm_file_exists_and_is_valid_json()
    {
        File.Exists(RealmPath()).Should().BeTrue(
            "AppHost mounts keycloak/ as Keycloak's import directory; a missing file means an empty realm");

        Realm().GetProperty("realm").GetString().Should().Be("pgsh",
            "the frontend and the API both resolve the realm by that name");
    }

    /// <summary>
    /// ⚠ <b>The defect that took Keycloak down on 17/09/2026.</b> JSON has no comments, so the reflex
    /// is a <c>"_comment"</c> key — and Keycloak refuses any property its representation does not know,
    /// aborting the import and stopping the server. Explanations belong in
    /// <c>keycloak/README.md</c>, which is not parsed by anything.
    /// </summary>
    [Fact]
    public void No_property_anywhere_is_an_underscore_prefixed_note()
    {
        var offenders = new List<string>();
        Walk(Realm(), "$", offenders);

        offenders.Should().BeEmpty(
            "Keycloak rejects unknown properties outright — a note here stops the container starting");

        static void Walk(JsonElement element, string path, List<string> offenders)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name.StartsWith('_'))
                            offenders.Add($"{path}.{property.Name}");

                        Walk(property.Value, $"{path}.{property.Name}", offenders);
                    }
                    break;

                case JsonValueKind.Array:
                    int index = 0;
                    foreach (var item in element.EnumerateArray())
                        Walk(item, $"{path}[{index++}]", offenders);
                    break;
            }
        }
    }

    /// <summary>
    /// Every role the file declares is one <see cref="Roles"/> names, and every role a user is given is
    /// one the realm declares. ⚠ A role spelled only in the file is a claim
    /// <c>KeycloakRoleTransformer</c> hands to an authorisation check that will never match it — the
    /// user logs in, sees an empty application, and nothing says why.
    /// </summary>
    [Fact]
    public void Every_realm_role_is_one_the_application_knows()
    {
        var realm = Realm();

        var declared = realm.GetProperty("roles").GetProperty("realm")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()!)
            .ToHashSet();

        string[] known =
        [
            Roles.Student, Roles.Scolarite, Roles.Secretaire,
            Roles.Professor, Roles.Employee, Roles.SuperUser,
        ];

        declared.Should().BeSubsetOf(known, "a role PGSH does not know grants nothing and explains nothing");

        var granted = realm.GetProperty("users").EnumerateArray()
            .SelectMany(u => u.GetProperty("realmRoles").EnumerateArray())
            .Select(r => r.GetString()!)
            .Distinct();

        // ⚠ Keycloak creates `default-roles-<realm>` itself, so it is legitimately granted without
        // being declared here — see the test below for what it is actually for.
        granted.Except([DefaultRolesComposite]).Should()
            .BeSubsetOf(declared, "Keycloak's import refuses a role assignment it cannot resolve");
    }

    /// <summary>Keycloak's own per-realm composite. Not a PGSH role, and not optional.</summary>
    private const string DefaultRolesComposite = "default-roles-pgsh";

    /// <summary>
    /// ⚠ <b>Every account carries <c>default-roles-pgsh</c>, and leaving it out logs everyone
    /// straight back out.</b>
    ///
    /// <para>The first version of this file gave each user only its PGSH roles. Keycloak then grants
    /// none of the <c>account</c> client roles, so the access token carries <b>no <c>aud</c> claim at
    /// all</b> — and the API requires the audience <c>account</c>. Every request therefore answered
    /// <b>401</b>, and <c>errorMiddleware</c> turns a 401 into <c>keycloak.logout()</c>: the user
    /// signs in successfully and is bounced to the login screen a moment later, with nothing anywhere
    /// saying why.</para>
    ///
    /// <para>⚠ <b>It is the opposite failure from a missing <c>User</c> row</b>, and they look the
    /// same from the outside. No row is a <b>403</b> and lands on the « profil absent » page; no
    /// audience is a <b>401</b> and logs you out. Both are « I logged in and could not use the
    /// application », and only the token tells them apart.</para>
    /// </summary>
    [Fact]
    public void Every_account_carries_the_default_roles_composite_that_puts_account_in_the_audience()
    {
        foreach (var user in Realm().GetProperty("users").EnumerateArray())
        {
            var roles = user.GetProperty("realmRoles").EnumerateArray()
                .Select(r => r.GetString()!)
                .ToList();

            roles.Should().Contain(DefaultRolesComposite,
                $"without it {user.GetProperty("email").GetString()} gets a token with no `aud`, "
                + "every API call answers 401, and the client logs the user out");
        }
    }

    /// <summary>
    /// ⚠ <b>The trap this pairing exists to close.</b> <c>UserContext.SyncAsync</c> links the Keycloak
    /// <c>sub</c> to a local <c>User</c> by <c>IdentityProviderId</c> and, failing that, <b>by
    /// e-mail</b>. With no row carrying that e-mail it throws <c>UserProfileNotFoundException</c> — a
    /// <b>403 « Profile Not Found »</b> after a login that Keycloak considered perfectly successful,
    /// naming nothing the operator can act on.
    ///
    /// <para>So the realm's accounts and <c>Seeder.SeedStaticUsersAsync</c> are one list. This reads
    /// the seeder as <i>text</i> rather than running it: what is being asserted is that the two
    /// fixtures agree, which is a fact about the files, not about a database.</para>
    /// </summary>
    [Fact]
    public void Every_account_in_the_realm_has_a_seeded_user_row()
    {
        string seeder = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "PGSH.MigrationService", "Seeder.cs"));

        var emails = Realm().GetProperty("users").EnumerateArray()
            .Select(u => u.GetProperty("email").GetString()!)
            .ToList();

        emails.Should().NotBeEmpty("a realm with no accounts is a realm nobody can log into");

        foreach (string email in emails)
        {
            seeder.Should().Contain(email,
                $"Keycloak account {email} would log in and then be refused 403 « Profile Not Found », "
                + "because no User row carries that address");
        }
    }

    /// <summary>
    /// The client the frontend asks for, as a public PKCE client. ⚠ <c>publicClient</c> with no
    /// <c>pkce.code.challenge.method</c> is a public client accepting a bare authorization code, and
    /// the frontend's adapter sends a challenge the server would then ignore.
    /// </summary>
    [Fact]
    public void The_frontend_client_is_public_and_requires_pkce()
    {
        var client = Realm().GetProperty("clients").EnumerateArray()
            .Single(c => c.GetProperty("clientId").GetString() == "pgsh-frontend");

        client.GetProperty("publicClient").GetBoolean().Should().BeTrue();
        client.GetProperty("standardFlowEnabled").GetBoolean().Should().BeTrue();
        client.GetProperty("attributes").GetProperty("pkce.code.challenge.method")
            .GetString().Should().Be("S256");
    }

    /// <summary>
    /// ⚠ <b>The client the API's own documentation UIs ask for has to be one the realm declares.</b>
    ///
    /// <para>Swagger was configured for <c>pgsh-swagger</c> — a client that existed only inside the
    /// Keycloak container's volume. The volume was lost on 17/09/2026, the realm was rebuilt from this
    /// file, and the literal in the code stopped naming anything: pressing « Authorize » in Scalar
    /// landed on Keycloak's <i>« Client not found »</i> page, which reads as a broken button and names
    /// no remedy. Nothing in the build could see it — a JSON file nothing compiles on one side, a
    /// string literal on the other.</para>
    ///
    /// <para>This is the join. <c>ApiDocumentationAuth.ClientId</c> is the one place the name is
    /// written, and here it must resolve against the realm — the same coupling this file already
    /// enforces between the realm's accounts and the <c>Seeder</c>'s.</para>
    /// </summary>
    [Fact]
    public void The_client_the_api_documentation_authenticates_through_exists_in_the_realm()
    {
        var clients = Realm().GetProperty("clients").EnumerateArray().ToList();

        var client = clients.SingleOrDefault(
            c => c.GetProperty("clientId").GetString() == ApiDocumentationAuth.ClientId);

        client.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            $"Scalar and Swagger authenticate as « {ApiDocumentationAuth.ClientId} », and a client the "
            + "realm does not declare makes « Authorize » a dead end. Declared here: "
            + string.Join(", ", clients.Select(c => c.GetProperty("clientId").GetString())));

        // It has to be able to carry the flow those UIs use, not merely exist.
        client.GetProperty("publicClient").GetBoolean().Should().BeTrue();
        client.GetProperty("standardFlowEnabled").GetBoolean().Should().BeTrue();
        client.GetProperty("attributes").GetProperty("pkce.code.challenge.method")
            .GetString().Should().Be("S256");
    }

    /// <summary>
    /// ⚠ <b>Keycloak expands a wildcard only at the <i>end</i> of a redirect URI.</b> A <c>*</c> in the
    /// middle — <c>http://localhost:*/*</c>, meant as « any local port » — is matched literally and
    /// therefore matches nothing.
    ///
    /// <para><b>This file carried two such entries and they never worked.</b> The frontend was fine
    /// because it also had a real trailing-wildcard entry; the API's documentation UIs, served on the
    /// API's own port, got « Invalid redirect URI » — the second dead end in a row on the same button,
    /// after « Client not found ».</para>
    ///
    /// <para>⚠ <b>And the first version of this test asserted the broken pattern and passed</b>, because
    /// it checked that the <i>file</i> contained <c>https://localhost:*</c> — not that Keycloak would do
    /// anything with it. That is precisely the trap <c>keycloak/README.md</c> names: this file verifies
    /// the contract, never the configuration. The rule below is the one the server actually applies.</para>
    /// </summary>
    [Fact]
    public void No_redirect_uri_hides_a_wildcard_anywhere_but_at_the_end()
    {
        var client = Realm().GetProperty("clients").EnumerateArray()
            .Single(c => c.GetProperty("clientId").GetString() == ApiDocumentationAuth.ClientId);

        var redirects = client.GetProperty("redirectUris").EnumerateArray()
            .Select(u => u.GetString()!)
            .ToList();

        foreach (string uri in redirects)
        {
            int star = uri.IndexOf('*');
            if (star < 0) continue;

            // The first star being the last character also settles « only one »: a second would make
            // the first one earlier than the end.
            star.Should().Be(uri.Length - 1,
                $"« {uri} » — Keycloak expands a wildcard only as the last character, so one placed "
                + "anywhere else is matched literally and admits nothing");
        }
    }

    /// <summary>
    /// The redirect back from Keycloak lands on the <b>API's</b> port, not the frontend's, so the client
    /// has to admit it explicitly — there is no « any port » pattern to lean on.
    /// </summary>
    /// <remarks>
    /// ⚠ The ports come from <c>PGSH.API/Properties/launchSettings.json</c>, which is what Aspire
    /// launches the API on. Two files that must agree, joined here rather than left to a « Invalid
    /// redirect URI » page that names neither of them.
    /// </remarks>
    [Theory]
    [InlineData("https://localhost:7014/scalar/v1")]
    [InlineData("https://localhost:7014/swagger/oauth2-redirect.html")]
    [InlineData("http://localhost:5199/scalar/v1")]
    public void The_realm_admits_the_api_documentation_redirect(string landing)
    {
        var client = Realm().GetProperty("clients").EnumerateArray()
            .Single(c => c.GetProperty("clientId").GetString() == ApiDocumentationAuth.ClientId);

        var redirects = client.GetProperty("redirectUris").EnumerateArray()
            .Select(u => u.GetString()!)
            .ToList();

        bool admitted = redirects.Any(pattern => pattern.EndsWith('*')
            ? landing.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : string.Equals(pattern, landing, StringComparison.Ordinal));

        admitted.Should().BeTrue(
            $"« Authorize » redirects to {landing}; declared: {string.Join(", ", redirects)}");
    }
}
