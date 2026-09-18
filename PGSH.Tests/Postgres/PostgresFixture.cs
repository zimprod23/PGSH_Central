using Microsoft.EntityFrameworkCore;
using Npgsql;
using PGSH.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

namespace PGSH.Tests.Postgres;

/// <summary>
/// A real PostgreSQL server, for the half of this repository's blind spot that no amount of compiling
/// can answer.
/// </summary>
/// <remarks>
/// <para><b>What the other three providers cannot do.</b> <c>UseInMemoryDatabase</c> ignores foreign
/// keys, unique indexes and <c>OnDelete</c>, and refuses <c>ExecuteDelete</c>/<c>ExecuteUpdate</c>
/// outright. <c>SqlTranslationTests</c> proves a query <i>compiles</i> to SQL and nothing about the
/// rows it returns. <c>NewSqliteContext</c> is relational, but it has no filtered indexes, no
/// <c>NULLS NOT DISTINCT</c>, and different type affinities. Every delete guard in this system is
/// written against a <c>RESTRICT</c> or a <c>CASCADE</c> that only a real server enforces.</para>
///
/// <para>⚠ <b>The schema comes from <c>EnsureCreated</c>, not from the migration chain</b>, and that
/// is deliberate rather than lazy. Three CNPN <i>data</i> migrations — <c>Cnpn1650Med3Stages</c>,
/// <c>Cnpn1650ImmersionStages</c>, <c>Cnpn1650Med3CatalogueAlignment</c> — open with
/// <c>RAISE EXCEPTION</c> because they need the <c>Levels</c> and <c>Stages</c> the legacy import
/// creates, so <c>MigrateAsync</c> against an empty server fails by design
/// (<c>docs/operations.md</c> §1). <c>EnsureCreated</c> builds the same tables, foreign keys, delete
/// behaviours and filtered indexes straight from the model.</para>
///
/// <para>⚠ <b>So state what this therefore does not prove:</b> that the migration chain produces the
/// model's schema. A migration that has drifted from its configuration is invisible here, exactly as
/// it is everywhere else in this suite. What is covered is the schema the code believes in, enforced
/// by the server that will enforce it in production.</para>
///
/// <para><b>Isolation.</b> One container per run; the schema is built once into a template database
/// and each test clones it with <c>CREATE DATABASE … TEMPLATE …</c>, which costs milliseconds. Tests
/// therefore share no rows at all — unlike <c>ApiFactory</c>, where rows one test wrote have made
/// three unrelated tests fail.</para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TemplateDatabase = "pgsh_template";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase(TemplateDatabase)
        .WithUsername("pgsh")
        .WithPassword("pgsh")
        .Build();

    private int _databaseCounter;

    /// <summary>
    /// Why the container could not be started, or <c>null</c> when it is running.
    /// </summary>
    /// <remarks>
    /// ⚠ Held rather than thrown so a machine without Docker gets <b>skipped</b> tests carrying a
    /// sentence, never green ones. A suite that quietly passes when it did not run is the failure
    /// mode this repository treats as worse than a red build.
    /// </remarks>
    public string? Unavailable { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            Unavailable = $"PostgreSQL container unavailable: {ex.Message}";
            return;
        }

        await using (var db = NewContext(TemplateDatabase))
            await db.Database.EnsureCreatedAsync();

        // A database cannot serve as a template while anything is connected to it, and Npgsql pools
        // the connection EnsureCreated just used.
        NpgsqlConnection.ClearAllPools();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// A database of this test's own, cloned from the template schema and holding no rows.
    /// </summary>
    public async Task<PostgresDatabase> NewDatabaseAsync()
    {
        string name = $"pgsh_test_{Interlocked.Increment(ref _databaseCounter)}";

        await using (var admin = new NpgsqlConnection(ConnectionString("postgres")))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"""CREATE DATABASE "{name}" TEMPLATE "{TemplateDatabase}" """;
            await create.ExecuteNonQueryAsync();
        }

        return new PostgresDatabase(() => NewContext(name));
    }

    private ApplicationDbContext NewContext(string database) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString(database))
            .Options);

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database,
        }.ConnectionString;
}

/// <summary>
/// One test's database. <see cref="Connect"/> opens a <b>fresh</b> context on it each time.
/// </summary>
/// <remarks>
/// ⚠ <b>Asking the server a question means asking it from a context that has not seen the answer.</b>
/// EF resolves a <c>RESTRICT</c> itself when the dependent rows are tracked — it severs the
/// association client-side and throws an <c>InvalidOperationException</c> before any SQL is sent — so
/// a test that seeds and deletes through one context is testing the change tracker, not the schema.
/// Seed through one context, then <see cref="Connect"/> a second one to perform the act.
/// </remarks>
public sealed class PostgresDatabase(Func<ApplicationDbContext> factory)
{
    public ApplicationDbContext Connect() => factory();
}

/// <summary>One container for the whole run, shared by every class in the collection.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
