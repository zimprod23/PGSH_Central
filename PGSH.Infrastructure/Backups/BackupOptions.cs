namespace PGSH.Infrastructure.Backups;

/// <summary>
/// Where dumps go, how they are taken, and how long the scheduled ones are kept. Bound from the
/// <c>Backups</c> configuration section.
/// </summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backups";

    /// <summary>
    /// Where the dumps are written. ⚠ Defaults <b>outside</b> the repository and outside the
    /// container's own volume — a backup living in the thing it is a backup of is not one, and
    /// <c>pgsh-postgres-data</c> is exactly that thing.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>
    /// The Postgres container to dump from. Left empty it is discovered from <c>docker ps</c>, which
    /// is what Aspire's generated name (<c>postgres-…</c>) makes necessary.
    /// </summary>
    public string? ContainerName { get; set; }

    /// <summary>Path to the docker CLI. Only worth setting when it is not on PATH.</summary>
    public string DockerPath { get; set; } = "docker";

    /// <summary>
    /// How long one dump may take. The live base is ~100 MB compressed; ten minutes is slack, not a
    /// target, and a runner that hangs forever would hold a request open behind it.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// How long the <b>probe</b> may take — « is there a Docker engine, and which container is ours ».
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Deux durées sans rapport portaient un seul nombre.</b> Un <c>docker version</c> répond en
    /// une seconde ou ne répondra pas ; un <c>pg_dump</c> de la base vivante prend des minutes. Avec
    /// les 600 s du dump appliquées aussi à la sonde, un moteur Docker en train de mourir — ce qui est
    /// arrivé le 13/09/2026, WSL se mettant à jour de lui-même — laissait l'écran des sauvegardes en
    /// attente <b>dix minutes</b>, alors que la phrase qui explique la situation existait déjà et
    /// n'attendait que de pouvoir être dite. Une sonde qui n'a pas répondu <i>est</i> une réponse.
    /// </remarks>
    public int ProbeTimeoutSeconds { get; set; } = 10;

    public ScheduleOptions Schedule { get; set; } = new();

    /// <summary>
    /// ⚠ Whether the Keycloak realm is dumped alongside the base. <b>False, and this version does not
    /// implement it</b> — it is reported to the screen precisely so the gap is stated rather than
    /// assumed away. Restoring the base without the matching realm leaves
    /// <c>SyncUserMiddleware</c> matching a Keycloak <c>sub</c> against <c>User</c> rows that are no
    /// longer there, and its fallback is the e-mail address.
    /// </summary>
    public bool KeycloakRealmCovered { get; set; }

    public sealed class ScheduleOptions
    {
        /// <summary>
        /// On by default. ⚠ The whole failure this feature exists for is a dump nobody remembered to
        /// take, so a scheduler that has to be switched on is one that will be switched on the day
        /// after it was needed.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// How often a scheduled point is taken. <b>Daily</b> (1 440 min) since 2026-09-04, at the
        /// user's request — an hourly <c>pg_dump</c> of the whole faculty base costs more disk and
        /// I/O than the recovery window is worth, and the acts that actually need an undo (a
        /// déliberation, a réinscription roll, an axis apply) take their own point from the dialog
        /// rather than relying on the timer.
        /// </summary>
        /// <remarks>
        /// ⚠ <b>Coupled to <c>SafePointEvaluator.DefaultFreshFor</c>.</b> Freshness means « the timer
        /// has not missed a run », so that constant is one interval plus one. Shortening this without
        /// widening that reports nothing wrong; <em>lengthening</em> this without widening that makes
        /// every point read stale between runs.
        /// </remarks>
        public int IntervalMinutes { get; set; } = 1440;

        /// <summary>
        /// Every scheduled point younger than this is kept, whatever the cadence; past it, retention
        /// thins to one a day. Named for what it does rather than for an hourly schedule — the
        /// interval is configurable and the tier has to stay true under any value of it.
        /// </summary>
        public int KeepAllForHours { get; set; } = 24;

        /// <summary>How long one point per day is kept after the hourly window has passed.</summary>
        public int KeepDailyForDays { get; set; } = 30;
    }
}
