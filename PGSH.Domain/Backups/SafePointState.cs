namespace PGSH.Domain.Backups;

/// <summary>
/// Where the base stands with respect to its most recent safe point — the one fact every bulk act's
/// confirmation shows before it lets somebody write a promotion's worth of rows.
/// </summary>
/// <remarks>
/// ⚠ <b><see cref="Unavailable"/> and <see cref="None"/> are two states, never one blank.</b> « the
/// backup service cannot be reached » and « there is no backup » call for opposite acts — fix the
/// runner, versus take a point — and a single « aucune sauvegarde » covering both is the same defect
/// as an omitted year read as « toutes les années » or an empty répartition that cannot say whether
/// the axis or the arrangement is missing.
///
/// <para>The order is precedence, worst first, and <see cref="SchemaChanged"/> deliberately outranks
/// <see cref="Stale"/>: an hour-old dump taken under a schema the running code no longer matches is
/// a restore that <em>refuses</em>, while a three-day-old one under the right schema is a restore
/// that works and merely costs three days.</para>
/// </remarks>
public enum SafePointState
{
    /// <summary>The archive could not be reached at all. Nothing is being backed up, and nobody was told.</summary>
    Unavailable = 0,

    /// <summary>The archive answers and holds nothing. There is no undo.</summary>
    None = 1,

    /// <summary>A point exists, and a migration has been applied since. Restoring it needs a schema step.</summary>
    SchemaChanged = 2,

    /// <summary>A point exists under the running schema, and it is older than the freshness window.</summary>
    Stale = 3,

    /// <summary>A recent point exists under the running schema.</summary>
    Fresh = 4,
}
