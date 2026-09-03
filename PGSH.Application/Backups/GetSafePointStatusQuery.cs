using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Backups;
using PGSH.SharedKernel;

namespace PGSH.Application.Backups;

/// <summary>
/// « Y a-t-il un retour en arrière pour ce que je m'apprête à faire ? » — the read behind the banner
/// on « Sauvegardes » and inside every bulk act's confirmation.
/// </summary>
/// <remarks>
/// Deliberately cheap: a probe, a directory listing and the running fingerprint. It is asked by every
/// confirmation dialog in the application, so it may not count rows or open a dump.
/// </remarks>
public sealed record GetSafePointStatusQuery : IQuery<SafePointStatusResponse>;

internal sealed class GetSafePointStatusQueryHandler(
    IBackupArchive archive,
    ISchemaFingerprintProvider fingerprints,
    IBackupScheduleClock schedule,
    ExecutionAuthorizer authorizer,
    IDateTimeProvider clock)
    : IQueryHandler<GetSafePointStatusQuery, SafePointStatusResponse>
{
    public async Task<Result<SafePointStatusResponse>> Handle(
        GetSafePointStatusQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(BackupErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<SafePointStatusResponse>(access.Error);

        var probe = await archive.ProbeAsync(cancellationToken);
        var running = await fingerprints.GetAsync(cancellationToken);

        var points = probe.Reachable
            ? await archive.ListAsync(cancellationToken)
            : [];

        var verdict = SafePointEvaluator.Evaluate(
            probe.Reachable, points.FirstOrDefault(), running, clock.UtcNow);

        return new SafePointStatusResponse(
            verdict.State,
            probe.Location,
            probe.Reason,
            verdict.Point?.ToResponse(running),
            verdict.Age is null ? null : (long)verdict.Age.Value.TotalMinutes,
            verdict.HasUsableUndo,
            running.LastMigration,
            running.GitSha,
            points.Count,
            schedule.NextRunUtc,
            schedule.KeycloakRealmCovered);
    }
}

/// <summary>
/// What the scheduler is going to do next, read without depending on the scheduler itself — the
/// status endpoint must answer whether or not a background service is running.
/// </summary>
public interface IBackupScheduleClock
{
    /// <summary>Null when nothing is scheduled, which is a state the screen names rather than hides.</summary>
    DateTime? NextRunUtc { get; }

    /// <summary>Whether the Keycloak realm is dumped alongside the base. False in this version.</summary>
    bool KeycloakRealmCovered { get; }
}
