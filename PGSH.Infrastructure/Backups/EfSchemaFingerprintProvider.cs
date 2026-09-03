using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PGSH.Application.Backups;
using PGSH.Domain.Backups;
using PGSH.Infrastructure.Database;

namespace PGSH.Infrastructure.Backups;

/// <summary>
/// Reads the running schema's identity: the last migration EF says is applied, and the sha of the
/// build that is serving.
/// </summary>
/// <remarks>
/// ⚠ <b>Neither half may throw.</b> This is on the path that answers « y a-t-il un retour en
/// arrière ? », and a status screen that 500s because the migrations table could not be read has
/// removed the one thing the operator opened it for. An unreadable half comes back null, and
/// <see cref="SchemaFingerprint"/> reads unknown as « cannot certify » rather than as agreement.
/// </remarks>
internal sealed class EfSchemaFingerprintProvider(
    ApplicationDbContext dbContext,
    IConfiguration configuration)
    : ISchemaFingerprintProvider
{
    public async Task<SchemaFingerprint> GetAsync(CancellationToken cancellationToken)
    {
        string? migration = await LastAppliedMigrationAsync(cancellationToken);
        return new SchemaFingerprint(migration, ResolveGitSha());
    }

    private async Task<string?> LastAppliedMigrationAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Ordinal, not chronological: EF names migrations with a sortable timestamp prefix, and
            // the provider returns them in application order — but a base restored from elsewhere can
            // hold them in any order on disk.
            var applied = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
            return applied.OrderBy(m => m, StringComparer.Ordinal).LastOrDefault();
        }
        catch (Exception)
        {
            // The in-memory provider has no migrations at all, and a base that is unreachable is
            // reported by the probe rather than by an exception here.
            return null;
        }
    }

    /// <summary>
    /// Configuration first — a deployed build is the case where nothing else is available — then the
    /// build's own informational version (SourceLink appends <c>+sha</c>), then the working tree.
    /// </summary>
    private string? ResolveGitSha()
    {
        string? configured = configuration["Backups:GitSha"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        string? informational = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (informational is not null && informational.Contains('+'))
            return informational[(informational.IndexOf('+') + 1)..];

        return ReadHeadFromWorkingTree();
    }

    private static string? ReadHeadFromWorkingTree()
    {
        try
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string git = Path.Combine(directory.FullName, ".git");

                if (System.IO.Directory.Exists(git))
                {
                    string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();

                    if (!head.StartsWith("ref:", StringComparison.Ordinal))
                        return head;

                    string reference = head[4..].Trim();
                    string path = Path.Combine(git, reference.Replace('/', Path.DirectorySeparatorChar));

                    return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
                }

                directory = directory.Parent;
            }
        }
        catch (Exception)
        {
            // A build outside a working tree has no sha, which is a fact and not a failure.
        }

        return null;
    }
}
