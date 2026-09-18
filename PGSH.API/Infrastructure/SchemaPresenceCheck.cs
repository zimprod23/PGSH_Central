using Microsoft.EntityFrameworkCore;
using PGSH.Infrastructure.Database;

namespace PGSH.API.Infrastructure;

/// <summary>
/// Answers, once at startup, whether the database this API is pointed at has actually been built —
/// and says so in words when it has not, instead of letting every screen discover it as a 500.
/// </summary>
/// <remarks>
/// <para><b>What it is for.</b> A database with no schema is not a bug and not drift: it is a
/// database nobody has restored. On 17/09/2026 it produced
/// <c>42P01: relation "public.Users" does not exist</c> from <c>SyncUserMiddleware</c> on the very
/// first authenticated request — so every screen answered 500 « une erreur serveur est survenue », and
/// the one fact that mattered (there is no data here; restore a safe point) appeared nowhere.</para>
///
/// <para>⚠ <b>Why a startup check rather than a classification in the exception handler.</b>
/// <c>CLAUDE.md</c> is deliberate that a <c>PostgresException</c> the server actually answered with —
/// a missing column, a violated constraint — stays a <b>500</b>, because filing real drift as
/// « service indisponible » makes it an operations incident nobody ever fixes. That rule is right and
/// this must not weaken it. « The schema is absent <i>entirely</i> » is a different question, it has
/// exactly one remedy, and it can be asked <b>once</b> rather than guessed at per request.</para>
///
/// <para>⚠ <b>It distinguishes « cannot reach » from « nothing there », because they call for opposite
/// acts.</b> An unreachable database is <c>DatabaseOutage</c>'s job — the API starts and answers 503
/// while somebody fixes the server. Only a connection that <i>succeeds</i> and finds no schema is this
/// condition. Conflating them would stop the API booting during an ordinary outage.</para>
///
/// <para>⚠ <b>It refuses to start, and that is the point.</b> An API serving 500s on every screen
/// looks broken; an API that did not start, with one line saying why, sends the operator to the
/// remedy. Same reasoning as <c>Worker.RefuseToBuildAnEmptyBaseAsync</c> on the migration side — an
/// infrastructure state must not wear the costume of a defect.</para>
/// </remarks>
internal static class SchemaPresenceCheck
{
    internal const string Explanation =
        "La base est joignable mais elle ne porte aucun schéma : cette base n'a jamais été " +
        "construite, ou elle a été perdue avec son volume. Ce n'est pas un défaut de l'application, " +
        "et les migrations ne la reconstruiront pas (trois d'entre elles sont des migrations de " +
        "données qui refusent une base vide, par conception). " +
        "Restaurez un point de sauvegarde : scripts/pgsh-restore.ps1 -List, puis " +
        "scripts/pgsh-restore.ps1 -Id <point>. " +
        "⚠ « dotnet restore » restaure des paquets NuGet, pas la base de données.";

    /// <summary>
    /// <c>true</c> when the API may serve. <c>false</c> — after logging the explanation — when the
    /// database answers and holds no schema. Also <c>true</c> when the database cannot be reached at
    /// all, which is a different state with a different remedy.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>It returns rather than throws, and that is a deliberate correction.</b> The first version
    /// threw: the operator got a stack trace, the debugger broke on it, and a condition whose whole
    /// point is « this is not a defect » arrived wearing the costume of one — the exact mistake this
    /// class exists to remove, reproduced by the class itself. The process still exits non-zero, so
    /// the orchestrator marks the resource failed rather than finished; what is gone is the
    /// exception.
    /// </remarks>
    public static async Task<bool> EnsureSchemaExistsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(SchemaPresenceCheck));

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        bool schemaExists;
        try
        {
            // ⚠ Asked of one table the application cannot work without, through the model rather than
            // a hand-written name, so renaming it in the model cannot leave this check asking about a
            // table that no longer exists and reporting a healthy base as empty.
            string usersTable = dbContext.Model.FindEntityType(typeof(Domain.Users.User))!
                .GetTableName()!;

            // ⚠ The identifier is QUOTED, and that is not a detail. `to_regclass` parses its argument
            // as an identifier, so an unquoted one is folded to lower case — `public.Users` asks about
            // `public.users`, which does not exist in a base whose tables are PascalCase. Measured
            // 17/09/2026 against the live server: unquoted `f`, quoted `t`, on the same existing
            // table. Unquoted, this check would have reported a perfectly restored database as empty
            // and refused to start the API on it.
            schemaExists = await dbContext.Database
                .SqlQuery<bool>($"select to_regclass({$"public.\"{usersTable}\""}) is not null as \"Value\"")
                .SingleAsync();
        }
        catch (Exception exception)
        {
            // ⚠ Unreachable is NOT empty. The API must still boot during an outage so that
            // DatabaseOutage can answer 503 with a sentence; refusing to start here would turn a
            // transient outage into « the application will not run ».
            logger.LogWarning(
                exception,
                "The schema presence check could not run; starting anyway. If the database is down, "
                + "requests will answer 503.");
            return true;
        }

        if (schemaExists)
            return true;

        logger.LogCritical("{Explanation}", Explanation);

        // ⚠ And to the console directly, because the log alone was not enough — measured 17/09/2026.
        // `UseSerilog(ReadFrom.Configuration)` with no `Serilog` section builds a logger with **no
        // sinks**, and it replaces the default providers, so `LogCritical` went nowhere at all: the
        // operator saw an API marked « Finished » with an error icon and not one word of why. A
        // message explaining why the application will not start must not itself depend on logging
        // having been configured — that is the one thing it cannot assume.
        Console.Error.WriteLine();
        Console.Error.WriteLine("================ PGSH — L'API NE DÉMARRE PAS ================");
        Console.Error.WriteLine(Explanation);
        Console.Error.WriteLine("=============================================================");
        Console.Error.WriteLine();
        Console.Error.Flush();

        // ⚠ Non-zero, so the orchestrator shows this as failed rather than as a clean shutdown: an API
        // that merely "finished" reads as something somebody stopped on purpose.
        Environment.ExitCode = 1;
        return false;
    }
}
