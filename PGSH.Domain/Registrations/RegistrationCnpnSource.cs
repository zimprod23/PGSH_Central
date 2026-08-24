namespace PGSH.Domain.Registrations;

/// <summary>
/// How a registration came to be governed by the CNPN it names. Same load-bearing role as
/// <see cref="RegistrationOutcomeSource"/>: the column records a decision that cannot be recomputed
/// later, so a reader has to be able to tell an authored rule from a carried-forward default from a
/// backfill.
/// </summary>
public enum RegistrationCnpnSource
{
    /// <summary>
    /// A <c>CnpnLevelEffectivity</c> row covered this (level, year) — the faculty authored the cut,
    /// and this registration fell inside it. The only source that can move a student between texts.
    /// </summary>
    Effectivity,

    /// <summary>The student's own stamp (<c>Student.CnpnVersionId</c>) at the moment he registered.</summary>
    StudentStamp,

    /// <summary>
    /// Carried from the student's most recent earlier registration, because he carried no stamp of
    /// his own. Stickiness lives in the parcours, not only in the denormalised field.
    /// </summary>
    CarriedForward,

    /// <summary>
    /// Resolved from the intake the student entered on, via <c>CnpnAssignment</c> — the ordinary
    /// case for a genuine new entrant, and the last resort for anyone else.
    /// </summary>
    ResolvedFromEntry,

    /// <summary>
    /// Written by the migration that introduced this column, from the student's stamp. It says "this
    /// is the best reading of a year that closed before PGSH recorded the question", and it is the
    /// one source that is never evidence of what the faculty decided at the time.
    /// </summary>
    Backfilled,
}
