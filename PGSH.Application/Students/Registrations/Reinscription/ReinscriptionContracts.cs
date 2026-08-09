using PGSH.Domain.Registrations;

namespace PGSH.Application.Students.Registrations.Reinscription;

/// <summary>What the réinscription will do about one student of the closed promotion.</summary>
public enum ReinscriptionAction
{
    /// <summary>A registration will be created in the target year, at <c>ToLevelLabel</c>.</summary>
    WillRegister,

    /// <summary>The year was never closed for this student — there is no verdict to derive anything from.</summary>
    NoOutcome,

    /// <summary>Diplômé, exclu or abandon: the course of study ended, so no year follows it.</summary>
    CursusEnded,

    /// <summary>The student already holds a registration in the target year. Re-running is safe.</summary>
    AlreadyRegistered,

    /// <summary>
    /// Admis, but no level exists above this one for the programme. Almost always a verdict that
    /// should have read « Diplômé » — reported so the PV can be corrected, never guessed at.
    /// </summary>
    NextLevelMissing,
}

public sealed record ReinscriptionRowReport(
    Guid StudentId,
    string StudentFullName,
    string? Cne,
    RegistrationStatus? Outcome,
    RegistrationOutcomeSource? OutcomeSource,
    ReinscriptionAction Action,
    string? ToLevelLabel,
    string Message);

/// <summary>
/// The dry run, and — after an apply — the record of what was created. Same shape both times because
/// it is the same plan.
/// </summary>
public sealed record ReinscriptionReport(
    string FromYearLabel,
    string ToYearLabel,
    string LevelLabel,
    int TotalConsidered,
    int WillRegister,
    int Skipped,
    // Rows needing a human decision before the rollover is complete — NoOutcome and NextLevelMissing.
    // Not blocking: the apply is idempotent, so these are fixed and the rollover re-run.
    int NeedsAttention,
    IReadOnlyDictionary<string, int> ByTargetLevel,
    IReadOnlyList<ReinscriptionRowReport> Rows);
