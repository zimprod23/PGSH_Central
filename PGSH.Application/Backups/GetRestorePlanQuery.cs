using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Backups;
using PGSH.SharedKernel;

namespace PGSH.Application.Backups;

/// <summary>
/// « Si je reviens à ce point, qu'est-ce que je perds ? » — read before a restore, and the reason the
/// restore dialog is allowed to exist at all.
/// </summary>
/// <remarks>
/// ⚠ <b>A destructive act nobody is shown a number for is one nobody agreed to</b> — the same rule as
/// <c>RostersRemoved</c>, <c>PlannedCellsRemoved</c> and the forced unpublish naming the marks it
/// takes. Here the numbers come from comparing the manifest's census with the base as it stands, so
/// « la restauration effacerait 6 813 inscriptions » is arithmetic rather than a warning.
///
/// <para>⚠ It reports both directions. Rows <em>written</em> since the point are what a restore
/// discards; rows <em>gone</em> since are what it brings back — and the second is as often the reason
/// for restoring as the first is the reason not to.</para>
///
/// <para>⚠ A schema mismatch does not fail this query. §18 asks a restore to refuse loudly on one, and
/// it does — but the refusal has to be able to say <em>which</em> <c>dotnet ef database update</c>
/// makes the point usable, and a query that failed could not.</para>
/// </remarks>
public sealed record GetRestorePlanQuery(string Id) : IQuery<RestorePlanResponse>;

internal sealed class GetRestorePlanQueryHandler(
    IBackupArchive archive,
    ISchemaFingerprintProvider fingerprints,
    DatabaseCensusReader census,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetRestorePlanQuery, RestorePlanResponse>
{
    public async Task<Result<RestorePlanResponse>> Handle(
        GetRestorePlanQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(BackupErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<RestorePlanResponse>(access.Error);

        var probe = await archive.ProbeAsync(cancellationToken);
        if (!probe.Reachable)
            return Result.Failure<RestorePlanResponse>(
                BackupErrors.Unavailable(probe.Reason ?? "raison inconnue"));

        var point = await archive.FindAsync(request.Id, cancellationToken);
        if (point is null)
            return Result.Failure<RestorePlanResponse>(BackupErrors.NotFound(request.Id));

        var running = await fingerprints.GetAsync(cancellationToken);
        var current = await census.ReadAsync(cancellationToken);

        var deltas = DatabaseCensus.Compare(point.Census, current);

        var impact = deltas
            .Select(d => new RestoreImpactLine(d.Table, d.AtSafePoint, d.Now, d.Written, d.Removed))
            .ToList();

        // Null when the point censused nothing comparable, never 0: « ce point n'en dit rien » and
        // « rien n'a changé » are different answers, and only one of them is a reason to proceed.
        long? discarded = deltas.All(d => d.IsUnknown) ? null : deltas.Sum(d => d.Written ?? 0);
        long? restored = deltas.All(d => d.IsUnknown) ? null : deltas.Sum(d => d.Removed ?? 0);

        bool schemaMatches = point.Schema.MatchesSchemaOf(running);

        return new RestorePlanResponse(
            point.ToResponse(running),
            schemaMatches,
            running.LastMigration,
            schemaMatches ? null : SchemaStepFor(point),
            archive.DescribeRestoreCommand(point),
            impact,
            discarded,
            restored,
            point.Id);
    }

    /// <summary>
    /// The migration the base has to be brought to before this dump can be read under the running
    /// code. Null when the point does not know its own migration — which is not « no step needed »,
    /// and the screen says so rather than printing a command that would be a guess.
    /// </summary>
    private static string? SchemaStepFor(BackupManifest point) =>
        string.IsNullOrWhiteSpace(point.Schema.LastMigration)
            ? null
            : $"dotnet ef database update {point.Schema.LastMigration} "
              + "--project PGSH.Infrastructure --startup-project PGSH.MigrationService";
}
