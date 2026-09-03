namespace PGSH.Domain.Backups;

/// <summary>
/// Why a point exists — which is what decides whether retention may remove it.
/// </summary>
/// <remarks>
/// ⚠ Retention prunes <see cref="Scheduled"/> and nothing else. A point somebody took by hand, or
/// took because he was about to apply a déliberation, is the only record of the state before an act
/// that cannot be undone; expiring it on a timer would remove the undo precisely for the acts that
/// have no other one.
/// </remarks>
public enum BackupKind
{
    /// <summary>Taken by the timer. Subject to the retention window.</summary>
    Scheduled = 0,

    /// <summary>Taken by hand from « Sauvegardes ». Kept until somebody removes it.</summary>
    Named = 1,

    /// <summary>Taken from a bulk act's own confirmation, immediately before applying it. Kept.</summary>
    PreAct = 2,
}
