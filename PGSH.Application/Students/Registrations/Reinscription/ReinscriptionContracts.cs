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

    /// <summary>
    /// Admis into what would be the <b>last year of his own cursus</b>, while a stage from an earlier
    /// year is still unvalidated. Refused: the final year cannot begin until everything below it is
    /// validated or revalidated. Cleared by revalidating the stage, or by a nominative
    /// <c>FinalYearEntryWaiver</c> — the faculty does grant exceptions, and one it cannot record is
    /// one that gets granted in SQL.
    /// </summary>
    FinalYearBlocked,
}

public static class ReinscriptionActionExtensions
{
    /// <summary>
    /// Rows a human has to do something about before the rollover is complete. Not blocking — the
    /// apply is idempotent — but they are the rows that must survive the cap on <c>Rows</c>, which is
    /// why they are ordered first.
    /// </summary>
    public static bool NeedsAttention(this ReinscriptionAction action) =>
        action is ReinscriptionAction.NoOutcome
               or ReinscriptionAction.NextLevelMissing
               or ReinscriptionAction.FinalYearBlocked;
}

public sealed record ReinscriptionRowReport(
    Guid StudentId,
    string StudentFullName,
    string? Cne,
    int FromLevelId,
    string FromLevelLabel,
    RegistrationStatus? Outcome,
    RegistrationOutcomeSource? OutcomeSource,
    ReinscriptionAction Action,
    string? ToLevelLabel,
    string Message);

/// <summary>How one promotion of the closing year rolls over. One entry per level, so it is bounded.</summary>
public sealed record ReinscriptionLevelBreakdown(
    int LevelId,
    string LevelLabel,
    int Considered,
    int WillRegister,
    int NeedsAttention);

/// <summary>
/// The dry run, and — after an apply — the record of what was created. Same shape both times because
/// it is the same plan.
/// </summary>
public sealed record ReinscriptionReport(
    string FromYearLabel,
    string ToYearLabel,
    string ScopeLabel,
    int TotalConsidered,
    int WillRegister,
    int Skipped,
    // Rows needing a human decision before the rollover is complete — NoOutcome and NextLevelMissing.
    // Not blocking: the apply is idempotent, so these are fixed and the rollover re-run.
    int NeedsAttention,
    /// <summary>
    /// Students refused entry into their final year over an unvalidated earlier stage. Broken out of
    /// <paramref name="NeedsAttention"/> because it is the one that has an <em>action</em> attached —
    /// revalidate, or grant a dérogation — where the others are corrections to the PV.
    /// </summary>
    int FinalYearBlocked,
    /// <summary>
    /// Students who entered their final year <em>because</em> a dérogation was granted. Counted so the
    /// exception is visible in the same report as the rule: an override nobody sees is an override
    /// nobody reviews.
    /// </summary>
    int FinalYearWaived,
    IReadOnlyDictionary<string, int> ByTargetLevel,
    IReadOnlyList<ReinscriptionLevelBreakdown> ByLevel,
    IReadOnlyList<ReinscriptionRowReport> Rows,
    // Rows is capped — a year-wide rollover considers every student of the faculty — and ordered so
    // the ones needing attention come first. The counts above stay exact.
    bool RowsTruncated);
