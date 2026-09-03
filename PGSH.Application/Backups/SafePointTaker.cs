using PGSH.Domain.Backups;
using PGSH.SharedKernel;

namespace PGSH.Application.Backups;

/// <summary>
/// Takes a safe point and decides <em>what it records</em>: the dump, plus the schema it was taken
/// under and the row counts that make a restore's cost a number.
/// </summary>
/// <remarks>
/// ⚠ It exists because there are two callers and they must not drift — the command a human (or a
/// confirmation dialog) sends, and the timer. A manifest written by one that the other cannot compare
/// against is a point with no undo attached to it, and the mistake would only show on the day of a
/// restore. Same reason <c>FinalYearGuard.EnsureMayEnterManyAsync</c> is the implementation and the
/// single-student call delegates to it.
///
/// <para>Public, unlike most of this layer's helpers, precisely because the timer lives in
/// Infrastructure: a background service has no <c>HttpContext</c> and so no caller for
/// <c>ExecutionAuthorizer</c> to judge. The authorisation therefore sits on the <em>command</em>,
/// where there is somebody to authorise, and never here.</para>
/// </remarks>
public sealed class SafePointTaker(
    IBackupArchive archive,
    ISchemaFingerprintProvider fingerprints,
    DatabaseCensusReader census)
{
    public async Task<Result<BackupManifest>> TakeAsync(
        string label,
        BackupKind kind,
        string? note,
        string? takenBy,
        CancellationToken cancellationToken)
    {
        var probe = await archive.ProbeAsync(cancellationToken);
        if (!probe.Reachable)
            return Result.Failure<BackupManifest>(
                BackupErrors.Unavailable(probe.Reason ?? "raison inconnue"));

        return await archive.CreateAsync(
            new BackupRequest(
                label,
                kind,
                note,
                takenBy,
                await fingerprints.GetAsync(cancellationToken),
                await census.ReadAsync(cancellationToken)),
            cancellationToken);
    }
}
