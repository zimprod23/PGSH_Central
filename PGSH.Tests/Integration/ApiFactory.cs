using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PGSH.Domain.Users;
using PGSH.Infrastructure.Database;

namespace PGSH.Tests.Integration;

/// <summary>
/// Hosts the real API in-process, so a test can reach an endpoint the way a browser does.
///
/// <para>⚠ This exists because a handler-level test cannot see the half of an endpoint that is not
/// the handler. Routing, the required-ness of a query parameter, model binding, authentication,
/// authorization, <c>SyncUserMiddleware</c>, the exception handler and the mapping from
/// <c>Result.Failure</c> to a problem response are all wired in <c>Program.cs</c>, and a guard is only
/// as good as the pipeline that reaches it. The « Retrait » refusal was the case that forced this:
/// once the level stopped being offered in the UI, the refusal became unreachable by hand, and the
/// manual smoke step for it went unexecuted two sessions running.</para>
/// </summary>
/// <remarks>
/// ⚠ The store is still <c>UseInMemoryDatabase</c>, so this closes the *pipeline* blind spot and not
/// the *store* one: FK constraints, unique indexes, <c>OnDelete</c> behaviour and SQL translatability
/// remain invisible. Testcontainers is the other half and is still not built — do not read a green
/// integration suite as proof that a query runs on PostgreSQL.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"api-{Guid.NewGuid()}";

    /// <summary>The caller every test uses unless it is testing who the caller is.</summary>
    public static readonly Guid AdminIdentityId = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": that branch maps the Scalar/Swagger UI, which has nothing to do with the
        // behaviour under test and pulls the whole documentation pipeline into every test run.
        builder.UseEnvironment("Testing");

        // Aspire's AddNpgsqlDbContext reads this while the host is being built — before
        // ConfigureTestServices can replace anything — and throws when it is absent. A well-formed
        // string pointing at nothing is enough to get past it; no connection is ever opened, because
        // the registration it produces is removed below.
        builder.UseSetting(
            "ConnectionStrings:TodoDatabase",
            "Host=localhost;Port=5432;Database=pgsh-integration;Username=none;Password=none");

        builder.ConfigureTestServices(services =>
        {
            UseInMemoryStore(services);
            UseTestAuthentication(services);
        });
    }

    private void UseInMemoryStore(IServiceCollection services)
    {
        // Everything Npgsql registered for this context, by service type rather than by name: the
        // options, the options configuration EF 9 adds, the context itself and its factory. Missing
        // one leaves the real provider in place and the first query fails on a connection refused,
        // which reads like a broken test rather than a broken replacement.
        var doomed = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || d.ServiceType == typeof(ApplicationDbContext)
                || d.ServiceType == typeof(IDbContextFactory<ApplicationDbContext>)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericArguments().Contains(typeof(ApplicationDbContext))
                    && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")))
            .ToList();

        foreach (var descriptor in doomed) services.Remove(descriptor);

        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(_databaseName));
    }

    private static void UseTestAuthentication(IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });

        // Program sets DefaultAuthenticateScheme and DefaultChallengeScheme to JwtBearer explicitly,
        // and an explicit value does not fall back to DefaultScheme. Configuring all three here — last,
        // so it wins — is what actually redirects the pipeline onto the test scheme.
        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = TestAuthHandler.Scheme;
            options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
            options.DefaultChallengeScheme = TestAuthHandler.Scheme;
        });
    }

    /// <summary>
    /// A client authenticated as <paramref name="identityId"/>. HTTPS because the pipeline runs
    /// <c>UseHttpsRedirection</c>, which answers a plain http request with a 307 that the test client
    /// does not follow — every assertion would then be about the redirect.
    /// </summary>
    public HttpClient CreateApiClient(Guid? identityId = null, params string[] roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (identityId ?? AdminIdentityId).ToString());
        if (roles.Length > 0) client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));

        return client;
    }

    /// <summary>A client carrying no identity at all — the anonymous caller.</summary>
    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    /// <summary>
    /// Runs <paramref name="seed"/> against the host's own store, then saves.
    /// <see cref="SeedCallingUser"/> is applied first: <c>SyncUserMiddleware</c> throws
    /// <c>UserProfileNotFoundException</c> on an authenticated request whose subject has no local
    /// <c>User</c>, so without it every request 500s before reaching any endpoint.
    /// </summary>
    public async Task SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        SeedCallingUser(db);
        seed(db);

        await db.SaveChangesAsync();
    }

    /// <summary>Reads the store back after a request, to assert what the endpoint actually wrote.</summary>
    public async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    /// <summary>
    /// Empties the store. The host — and so the store — is shared by every test in a class, and a test
    /// that writes leaves its rows behind: when the guard under test was temporarily removed to check
    /// these tests bite, the two that should have failed took two unrelated ones down with them,
    /// because the labels one test wrote were still there when the next asserted none existed. A
    /// cascade like that hides which assertion actually broke.
    /// </summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
    }

    private static void SeedCallingUser(ApplicationDbContext db)
    {
        if (db.Users.Any(u => u.IdentityProviderId == AdminIdentityId.ToString())) return;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{AdminIdentityId}@integration.test",
            FirstName = "Integration",
            LastName = "Caller",
        };
        user.LinkIdentity(AdminIdentityId.ToString());
        db.Users.Add(user);
    }
}
