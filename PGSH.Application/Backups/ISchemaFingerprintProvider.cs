using PGSH.Domain.Backups;

namespace PGSH.Application.Backups;

/// <summary>
/// What code the base is running under right now: the last applied EF migration, and the build's git
/// sha. Both are facts about the <em>host</em> rather than about any aggregate, which is why this is a
/// port and not a query.
/// </summary>
public interface ISchemaFingerprintProvider
{
    /// <summary>
    /// ⚠ Never throws. This is read on the path that answers « is there an undo? », and a status
    /// screen that 500s because the migrations table could not be read has removed the one thing the
    /// operator came for. An unreadable half comes back null, and <see cref="SchemaFingerprint"/>
    /// treats unknown as « cannot certify », not as « matches ».
    /// </summary>
    Task<SchemaFingerprint> GetAsync(CancellationToken cancellationToken);
}
