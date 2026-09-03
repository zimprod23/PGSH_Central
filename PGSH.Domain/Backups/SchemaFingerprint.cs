namespace PGSH.Domain.Backups;

/// <summary>
/// What code a dump was taken under: the last applied EF migration, and the git sha of the build.
/// </summary>
/// <remarks>
/// ⚠ <b>The manifest is the point, not the dump.</b> A dump taken before a migration and restored
/// under code that expects the new schema gives a base the running application cannot read — and
/// nothing about the file says so. The migration is the load-bearing half: it is the one a restore
/// can act on, because it names the <c>dotnet ef database update</c> that goes with the file. The
/// sha only explains <em>which build</em> wrote it.
///
/// <para>Either half may be unknown — a build outside a git working tree has no sha, and a base
/// nobody has migrated has no migration. Unknown is <b>not</b> a match: it is the absence of the
/// evidence a match is made of, and treating it as agreement is what silently allows the restore
/// this type exists to refuse.</para>
/// </remarks>
public sealed record SchemaFingerprint(string? LastMigration, string? GitSha)
{
    public static readonly SchemaFingerprint Unknown = new(null, null);

    /// <summary>Whether both halves are on record. A fingerprint that knows nothing cannot certify anything.</summary>
    public bool IsKnown => !string.IsNullOrWhiteSpace(LastMigration);

    /// <summary>
    /// Whether a dump carrying this fingerprint can be restored under <paramref name="running"/>
    /// without a schema step. Only the migration is compared — the same schema built from two shas is
    /// still the same schema, and refusing on the sha would refuse every restore taken before the last
    /// commit, which is all of them.
    /// </summary>
    public bool MatchesSchemaOf(SchemaFingerprint running) =>
        IsKnown
        && running.IsKnown
        && string.Equals(LastMigration, running.LastMigration, StringComparison.Ordinal);
}
