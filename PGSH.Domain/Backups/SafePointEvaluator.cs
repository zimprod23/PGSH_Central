namespace PGSH.Domain.Backups;

/// <summary>
/// Reads « is there an undo for what I am about to do? » off the archive. Pure — no store, no clock,
/// no process — like <c>WorkingDayCalendar</c>, <c>PeriodAxis</c> and <c>OccupancyTimeline</c>, and
/// for the same reason: this is the sentence shown on every irreversible act's confirmation, so its
/// cases are worth stating exactly rather than seeding approximately.
/// </summary>
public static class SafePointEvaluator
{
    /// <summary>
    /// How recent a point has to be to read as <see cref="SafePointState.Fresh"/>.
    /// </summary>
    /// <remarks>
    /// Twenty-four hours, matched to the scheduled hourly dump: anything longer and the timer has
    /// missed a run, which is a fact worth surfacing on its own. It is <em>not</em> a threshold for
    /// « safe to apply » — the acts this warns about write a promotion each, so what matters to the
    /// operator is that a point was taken since the last one, which is exactly what the
    /// « Créer un point maintenant » button in the dialog is for.
    /// </remarks>
    public static readonly TimeSpan DefaultFreshFor = TimeSpan.FromHours(24);

    public static SafePointVerdict Evaluate(
        bool archiveReachable,
        BackupManifest? latest,
        SchemaFingerprint running,
        DateTime nowUtc,
        TimeSpan? freshFor = null)
    {
        if (!archiveReachable)
            return new SafePointVerdict(SafePointState.Unavailable, null, null);

        if (latest is null)
            return new SafePointVerdict(SafePointState.None, null, null);

        // Clamped at zero: a point stamped in the future is a clock disagreement between the API host
        // and whatever wrote it, and a negative age would read as fresh by arithmetic accident.
        var age = nowUtc - latest.TakenAtUtc;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;

        if (!latest.Schema.MatchesSchemaOf(running))
            return new SafePointVerdict(SafePointState.SchemaChanged, latest, age);

        return age <= (freshFor ?? DefaultFreshFor)
            ? new SafePointVerdict(SafePointState.Fresh, latest, age)
            : new SafePointVerdict(SafePointState.Stale, latest, age);
    }
}

/// <summary>
/// The verdict and the evidence behind it. <paramref name="Point"/> is null exactly when there is no
/// point to name — <see cref="SafePointState.Unavailable"/> or <see cref="SafePointState.None"/>.
/// </summary>
public sealed record SafePointVerdict(SafePointState State, BackupManifest? Point, TimeSpan? Age)
{
    /// <summary>
    /// Whether the act about to be confirmed has an undo behind it. False for every state but
    /// <see cref="SafePointState.Fresh"/> and <see cref="SafePointState.Stale"/> — a point under
    /// another schema is one a restore refuses, which is not an undo.
    /// </summary>
    public bool HasUsableUndo => State is SafePointState.Fresh or SafePointState.Stale;
}
