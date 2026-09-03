using PGSH.Domain.Backups;
using PGSH.SharedKernel;

namespace PGSH.Application.Backups;

/// <summary>
/// The store of dumps and their manifests. The Application layer decides <em>when</em> a safe point
/// is taken and <em>what</em> it must record; this port knows how to actually run <c>pg_dump</c> and
/// where the files live — the same split as <c>IExportWorkbookWriter</c> / <c>ClosedXml…</c>, in the
/// other direction.
/// </summary>
/// <remarks>
/// ⚠ <b>Nothing here restores.</b> A process cannot replace the database it is serving from, and an
/// endpoint that tried would be asking the API to drop the ground it stands on. Restoring is an
/// operator act run at a terminal; what this port owes it is <see cref="DescribeRestoreCommand"/> —
/// the exact command, for the exact point, with the schema step it needs.
/// </remarks>
public interface IBackupArchive
{
    /// <summary>
    /// Whether the archive can be written to at all, and — when it cannot — why, in a sentence an
    /// operator can act on.
    /// </summary>
    /// <remarks>
    /// ⚠ Called before every listing, because « the runner is missing » must never reach a screen as
    /// « there is no backup ». Those are the two states <see cref="SafePointState"/> keeps apart.
    /// </remarks>
    Task<BackupArchiveProbe> ProbeAsync(CancellationToken cancellationToken);

    /// <summary>Every point on disk, newest first.</summary>
    Task<IReadOnlyList<BackupManifest>> ListAsync(CancellationToken cancellationToken);

    Task<BackupManifest?> FindAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a dump and writes its manifest beside it. Fails rather than throws: a backup that could
    /// not be taken is an ordinary refusal an operator has to read, not a 500.
    /// </summary>
    Task<Result<BackupManifest>> CreateAsync(BackupRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the archive's table of contents back. Proves the file is neither truncated nor corrupt —
    /// which is the failure the piped <c>pg_dump</c> produced here once — and nothing more.
    /// </summary>
    Task<Result<BackupManifest>> VerifyAsync(string id, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>The command an operator runs to restore this point, printed verbatim on the screen.</summary>
    string DescribeRestoreCommand(BackupManifest manifest);
}

/// <summary>
/// What a request for a new point carries. The census and the fingerprint are gathered by the
/// Application — they are facts about the database, which the archive has no business querying.
/// </summary>
public sealed record BackupRequest(
    string Label,
    BackupKind Kind,
    string? Note,
    string? TakenBy,
    SchemaFingerprint Schema,
    DatabaseCensus Census);

/// <summary>
/// <paramref name="Reason"/> is set exactly when <paramref name="Reachable"/> is false, and it is the
/// sentence the screen prints — « Docker est introuvable », not « erreur ».
/// </summary>
public sealed record BackupArchiveProbe(bool Reachable, string Location, string? Reason);
