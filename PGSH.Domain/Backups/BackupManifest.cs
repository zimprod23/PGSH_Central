namespace PGSH.Domain.Backups;

/// <summary>
/// Everything known about one safe point. Written beside its dump as JSON, and the only thing a
/// restore reads before deciding whether it may proceed.
/// </summary>
/// <remarks>
/// ⚠ <b>The manifest lives on disk, not in the database.</b> A registry kept in the base would be
/// rolled back by the very restore it describes: every point taken after the restored one would
/// vanish from the record while its file sat on disk, and the operator would be reading a list that
/// disagrees with the directory. The directory is therefore the register — it survives the act it
/// exists to document — and it is also why shipping this needs no migration against a live base.
///
/// <para><see cref="Id"/> is the dump's file stem and the address a restore is asked for by
/// (<c>20260903-142211-avant-reinscription</c>). <see cref="Label"/> is what a human wrote and is
/// deliberately not the key: two points taken before two runs of the same act want the same words.</para>
/// </remarks>
public sealed record BackupManifest(
    string Id,
    string Label,
    BackupKind Kind,
    DateTime TakenAtUtc,
    long SizeBytes,
    SchemaFingerprint Schema,
    DatabaseCensus Census,
    string? Note,
    string? TakenBy,
    BackupVerification Verification,
    DateTime? VerifiedAtUtc)
{
    public const string DumpExtension = ".dump";
    public const string ManifestExtension = ".manifest.json";

    public string FileName => Id + DumpExtension;
    public string ManifestFileName => Id + ManifestExtension;

    /// <summary>Retention may only ever remove a scheduled point — see <see cref="BackupKind"/>.</summary>
    public bool IsPrunable => Kind == BackupKind.Scheduled;

    /// <summary>
    /// Raises the recorded verification, never lowers it. Listing an archive that was already restored
    /// into a scratch base proves nothing new, and letting the weaker act overwrite the stronger one
    /// would quietly turn a proven dump back into a hypothesis.
    /// </summary>
    public BackupManifest MarkVerified(BackupVerification level, DateTime atUtc) =>
        level <= Verification ? this : this with { Verification = level, VerifiedAtUtc = atUtc };
}
