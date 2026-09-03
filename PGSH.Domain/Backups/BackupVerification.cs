namespace PGSH.Domain.Backups;

/// <summary>
/// How far anybody has actually gone in reading a dump back.
/// </summary>
/// <remarks>
/// ⚠ <b>A backup nobody has restored is a hypothesis.</b> The three values are deliberately ordered
/// by how much they prove: that a file exists proves nothing, that <c>pg_restore -l</c> can list its
/// table of contents proves the archive is not truncated or corrupt, and only a restore into a
/// scratch database proves the thing this feature exists for. A single boolean would have collapsed
/// the first two, which is where the corrupted dump of the piped-<c>pg_dump</c> incident sat.
/// </remarks>
public enum BackupVerification
{
    /// <summary>Written, never read back.</summary>
    Never = 0,

    /// <summary>Its table of contents was listed — the archive is readable and complete.</summary>
    Listed = 1,

    /// <summary>Restored into a scratch database, and the row counts asserted.</summary>
    Restored = 2,
}
