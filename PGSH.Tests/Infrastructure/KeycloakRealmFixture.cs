using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace PGSH.Tests.Infrastructure;

/// <summary>
/// One Keycloak, started once, with the repository's own <c>keycloak/</c> directory mounted as its
/// import source.
/// </summary>
/// <remarks>
/// <para>⚠ <b>A fixture and not <see cref="IAsyncLifetime"/> on the test class, for a measured
/// reason.</b> xUnit runs a class's <c>InitializeAsync</c> <b>per test</b>, so the first version of
/// these tests started eleven Keycloak containers and took <b>7 m 44 s</b> — in a suite that otherwise
/// finishes in under a minute. A test tier that slow is a test tier somebody excludes from the run,
/// and an excluded test protects nothing. <see cref="IClassFixture{TFixture}"/> starts it once.</para>
///
/// <para>⚠ <b>Nothing here writes to the realm.</b> Every case is a read of what Keycloak emits, so
/// one container is safely shared — unlike <c>PostgresFixture</c>, where each test takes its own
/// database precisely because tests there write.</para>
///
/// <para>⚠ <b>The mount is the repository directory, read-only.</b> Copying the realm into the test
/// project would create a second file to keep in step with the first, and the whole point is to
/// exercise the one that actually ships.</para>
/// </remarks>
public sealed class KeycloakRealmFixture : IAsyncLifetime
{
    private const string Image = "quay.io/keycloak/keycloak:25.0";

    public const string Realm = "pgsh";
    public const string ClientId = "pgsh-frontend";

    /// <summary>The password every account in the realm file carries. Development realm — see keycloak/README.md.</summary>
    public const string Password = "123";

    private IContainer? _keycloak;
    private HttpClient? _http;

    /// <summary>False when Docker is absent, so the cases skip with a sentence instead of failing.</summary>
    public bool Available { get; private set; }

    internal static string RepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PGSH.sln")))
            directory = directory.Parent;

        return directory!.FullName;
    }

    public async Task InitializeAsync()
    {
        if (!Postgres.Docker.Available)
            return;

        _keycloak = new ContainerBuilder()
            .WithImage(Image)
            .WithEnvironment("KEYCLOAK_ADMIN", "admin")
            .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
            .WithBindMount(
                Path.Combine(RepositoryRoot(), "keycloak"),
                "/opt/keycloak/data/import",
                AccessMode.ReadOnly)
            .WithCommand("start-dev", "--import-realm")
            .WithPortBinding(8080, assignRandomHostPort: true)
            // ⚠ Waiting on the realm's own endpoint, not on the port: the container answering means
            // Keycloak started, which it also does when the import silently produced nothing. This
            // 404s until `pgsh` exists, so a realm that failed to import fails the wait.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath($"/realms/{Realm}")))
            .Build();

        await _keycloak.StartAsync();

        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{_keycloak.Hostname}:{_keycloak.GetMappedPublicPort(8080)}"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        Available = true;
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();

        if (_keycloak is not null)
            await _keycloak.DisposeAsync();
    }

    /// <summary>
    /// The decoded payload of an access token for <paramref name="username"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ Failing to obtain one at all is itself a result worth reporting precisely: it means the realm
    /// did not import, or the account cannot authenticate — not that some claim is missing.
    /// </remarks>
    public async Task<(bool Ok, string Failure, JsonElement Payload)> TokenFor(string username)
    {
        var response = await _http!.PostAsync(
            $"/realms/{Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["username"] = username,
                ["password"] = Password,
                ["grant_type"] = "password",
            }));

        if (!response.IsSuccessStatusCode)
        {
            return (false,
                $"{username} could not obtain a token: {(int)response.StatusCode} "
                + await response.Content.ReadAsStringAsync(),
                default);
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        string jwt = body.GetProperty("access_token").GetString()!;

        return (true, string.Empty, DecodePayload(jwt));
    }

    private static JsonElement DecodePayload(string jwt)
    {
        string payload = jwt.Split('.')[1]
            .Replace('-', '+')
            .Replace('_', '/');

        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }
}
