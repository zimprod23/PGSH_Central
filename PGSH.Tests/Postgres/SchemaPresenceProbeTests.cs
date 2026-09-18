using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Users;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Postgres;

/// <summary>
/// The probe <c>SchemaPresenceCheck</c> uses to decide « has this database ever been built? », asked
/// of a real server in both states.
/// </summary>
/// <remarks>
/// <para>⚠ <b>This exists because the first version of that probe was wrong in the dangerous
/// direction.</b> It asked <c>to_regclass('public.Users')</c> — unquoted — and
/// <c>to_regclass</c> parses its argument as an identifier, so PostgreSQL folded it to
/// <c>public.users</c>, which does not exist in a base whose tables are PascalCase. The probe
/// therefore answered « no schema » on a **perfectly restored database**, and the check built on it
/// would have refused to start the API on exactly the database somebody had just spent an incident
/// recovering.</para>
///
/// <para>⚠ <b>Only a real server can answer this.</b> Identifier folding is PostgreSQL's, not EF's:
/// the in-memory provider has no notion of it, SQLite folds differently, and
/// <c>SqlTranslationTests</c> would have happily compiled the broken form. It is the same lesson as
/// the rest of this tier — a green suite that never asked the server proves nothing about the
/// server.</para>
///
/// <para>The two cases are the two states the check must tell apart, and a probe that gets either one
/// wrong is worse than no probe: a false « empty » bricks a healthy application, a false « built »
/// returns the 500-on-every-screen this was written to remove.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class SchemaPresenceProbeTests(PostgresFixture postgres)
{
    /// <summary>The probe exactly as <c>SchemaPresenceCheck</c> issues it.</summary>
    private static async Task<bool> SchemaExists(ApplicationDbContext context)
    {
        string usersTable = context.Model.FindEntityType(typeof(User))!.GetTableName()!;

        return await context.Database
            .SqlQuery<bool>($"select to_regclass({$"public.\"{usersTable}\""}) is not null as \"Value\"")
            .SingleAsync();
    }

    /// <summary>
    /// A database built from the model answers <b>true</b>. ⚠ This is the case the unquoted probe got
    /// wrong, and getting it wrong means refusing to serve a database that is perfectly fine.
    /// </summary>
    [PostgresFact]
    public async Task A_built_schema_is_reported_as_present()
    {
        postgres.Unavailable.Should().BeNull();

        await using var context = (await postgres.NewDatabaseAsync()).Connect();

        (await SchemaExists(context))
            .Should().BeTrue("the tables exist, so the API must be allowed to start");
    }

    /// <summary>
    /// A database with no tables answers <b>false</b> — the state a lost volume leaves behind, and the
    /// one whose remedy is a restore rather than a migration.
    /// </summary>
    [PostgresFact]
    public async Task An_empty_database_is_reported_as_absent()
    {
        postgres.Unavailable.Should().BeNull();

        await using var context = (await postgres.NewDatabaseAsync()).Connect();

        // Drop what EnsureCreated built, leaving a reachable database with nothing in it: exactly what
        // the API found on 17/09/2026 after the Docker volume was lost.
        await context.Database.ExecuteSqlRawAsync("drop schema public cascade; create schema public;");

        (await SchemaExists(context))
            .Should().BeFalse("nothing is there, and the remedy is a restore — not a 500 on every screen");
    }

    /// <summary>
    /// ⚠ The defect itself, pinned as a fact about PostgreSQL rather than as a comment: the unquoted
    /// form disagrees with the quoted one on a table that exists. If a future edit drops the quotes,
    /// this is what says why it must not.
    /// </summary>
    [PostgresFact]
    public async Task An_unquoted_identifier_is_folded_and_misses_a_pascal_case_table()
    {
        postgres.Unavailable.Should().BeNull();

        await using var context = (await postgres.NewDatabaseAsync()).Connect();

        bool unquoted = await context.Database
            .SqlQuery<bool>($"select to_regclass({"public.Users"}) is not null as \"Value\"").SingleAsync();

        bool quoted = await context.Database
            .SqlQuery<bool>($"select to_regclass({"public.\"Users\""}) is not null as \"Value\"").SingleAsync();

        quoted.Should().BeTrue("the table is there");
        unquoted.Should().BeFalse(
            "PostgreSQL folds an unquoted identifier to lower case, so this asks about public.users");
    }
}
