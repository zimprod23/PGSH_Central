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

        public int IntervalMinutes { get; set; } = 60;

        /// <summary>How long an hourly point is kept before retention may remove it.</summary>
        public int KeepHourlyForHours { get; set; } = 24;

        /// <summary>How long one point per day is kept after the hourly window has passed.</summary>
        public int KeepDailyForDays { get; set; } = 30;
    }
}
