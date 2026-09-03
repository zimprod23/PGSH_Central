using PGSH.Domain.Backups;

namespace PGSH.Application.Backups;

internal static class BackupMapping
{
    public static BackupPointResponse ToResponse(this BackupManifest manifest, SchemaFingerprint running) =>
        new(
            manifest.Id,
            manifest.Label,
            manifest.Kind,
            manifest.TakenAtUtc,
            manifest.SizeBytes,
            manifest.Schema.LastMigration,
            manifest.Schema.GitSha,
            manifest.Note,
            manifest.TakenBy,
            manifest.Verification,
            manifest.VerifiedAtUtc,
            manifest.Schema.MatchesSchemaOf(running),
            DatabaseCensus.Tables
                .Select(table => new CensusLine(table, manifest.Census[table]))
                .ToList());
}
