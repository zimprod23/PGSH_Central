using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PGSH.Application.Backups;
using PGSH.Domain.Backups;
using PGSH.SharedKernel;

namespace PGSH.Infrastructure.Backups;

/// <summary>
/// The dump nobody had to remember to take, plus the retention that keeps the directory finite.
/// </summary>
/// <remarks>
/// ⚠ <b>It goes through <c>SafePointTaker</c> rather than sending <c>CreateBackupPointCommand</c>.</b>
/// That command asks <c>ExecutionAuthorizer</c> whether the caller is administrative, and the timer
/// has no caller — there is no <c>HttpContext</c>, so <c>IUserContext</c> has nobody to be. Routing it
/// through MediatR would have meant either a fake identity or a hole in the guard, and the guard
/// exists for HTTP callers. What must <em>not</em> differ between the two paths is what the manifest
/// records, which is exactly what the taker owns.
///
/// <para>⚠ <b>Retention removes scheduled points only</b> (<see cref="BackupManifest.IsPrunable"/>).
/// A point somebody took by hand, or that a confirmation dialog took immediately before applying a
/// déliberation, is the only record of the state before an act that has no other undo; expiring it on
/// a timer would remove the undo precisely where there is no second one.</para>
///
/// <para>A failed run is logged and the timer continues. A backup service that stopped at the first
/// failure would be silently absent from then on, which is the state this whole phase exists to make
/// impossible.</para>
/// </remarks>
internal sealed class ScheduledBackupService(
    IServiceScopeFactory scopes,
    IOptions<BackupOptions> options,
    IDateTimeProvider clock,
    ILogger<ScheduledBackupService> logger)
    : BackgroundService, IBackupScheduleClock
{
    private readonly BackupOptions _options = options.Value;

    public DateTime? NextRunUtc { get; private set; }

    public bool KeycloakRealmCovered => _options.KeycloakRealmCovered;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Schedule.Enabled)
        {
            logger.LogInformation("Sauvegardes planifiées désactivées (Backups:Schedule:Enabled).");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(5, _options.Schedule.IntervalMinutes));

        // A first dump immediately would land in the middle of every startup, including the ones that
        // are only a rebuild. The first point of a session is taken one interval in — or by hand, or
        // by the confirmation of whatever bulk act comes first, which is the path that matters.
        NextRunUtc = clock.UtcNow + interval;

        using var timer = new PeriodicTimer(interval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
            NextRunUtc = clock.UtcNow + interval;
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try { return await timer.WaitForNextTickAsync(stoppingToken); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();

            var archive = scope.ServiceProvider.GetRequiredService<IBackupArchive>();

            var probe = await archive.ProbeAsync(cancellationToken);
            if (!probe.Reachable)
            {
                logger.LogWarning("Sauvegarde planifiée impossible : {Reason}", probe.Reason);
                return;
            }

            var created = await scope.ServiceProvider
                .GetRequiredService<SafePointTaker>()
                .TakeAsync(
                    "Sauvegarde automatique",
                    BackupKind.Scheduled,
                    note: null,
                    takenBy: null,
                    cancellationToken);

            if (created.IsFailure)
            {
                logger.LogWarning("Sauvegarde planifiée échouée : {Error}", created.Error.Description);
                return;
            }

            await PruneAsync(archive, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sauvegarde planifiée interrompue par une erreur.");
        }
    }

    /// <summary>
    /// Hourly points for the first window, then one a day, then nothing.
    /// </summary>
    /// <remarks>
    /// The kept day is the <em>newest</em> of each day rather than the oldest: on the day something
    /// went wrong, the point closest to it is the one worth having.
    /// </remarks>
    private async Task PruneAsync(IBackupArchive archive, CancellationToken cancellationToken)
    {
        var points = await archive.ListAsync(cancellationToken);
        var now = clock.UtcNow;

        var hourlyWindow = now - TimeSpan.FromHours(Math.Max(1, _options.Schedule.KeepHourlyForHours));
        var dailyWindow = now - TimeSpan.FromDays(Math.Max(1, _options.Schedule.KeepDailyForDays));

        var prunable = points.Where(p => p.IsPrunable).ToList();

        var keptPerDay = prunable
            .Where(p => p.TakenAtUtc <= hourlyWindow && p.TakenAtUtc > dailyWindow)
            .GroupBy(p => DateOnly.FromDateTime(p.TakenAtUtc))
            .Select(day => day.MaxBy(p => p.TakenAtUtc)!.Id)
            .ToHashSet();

        foreach (var point in prunable)
        {
            bool keep = point.TakenAtUtc > hourlyWindow
                        || keptPerDay.Contains(point.Id);

            if (keep)
                continue;

            var deleted = await archive.DeleteAsync(point.Id, cancellationToken);

            if (deleted.IsFailure)
                logger.LogWarning("Rotation : {Id} non supprimé — {Error}", point.Id, deleted.Error.Description);
        }
    }
}
