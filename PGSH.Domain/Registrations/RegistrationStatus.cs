namespace PGSH.Domain.Registrations;

/// <summary>
/// Where a student's academic year stands. The first two are positions the year passes through; the
/// rest are <em>outcomes</em> — a verdict the faculty pronounced in deliberation, recorded through
/// <see cref="Registration.RecordYearOutcome"/> and never by assignment.
/// </summary>
/// <remarks>
/// ⚠ Not to be confused with <c>InternshipAssignment.Status</c>/<c>Result</c>, which are one level
/// down and concern a single stage. PGSH is not linked to the pedagogical side of the faculty, so it
/// cannot compute any of the outcomes below — they arrive from the déliberation canvas.
/// </remarks>
public enum RegistrationStatus
{
    Pending,
    Active,

    /// <summary>Admis — the year is cleared and the student moves up a level.</summary>
    Validated,

    /// <summary>Redoublant — the year is not cleared and the student repeats the same level.</summary>
    Failed,

    /// <summary>Abandon — the student left of their own accord. Ends the cursus.</summary>
    Withdrawn,

    /// <summary>
    /// Diplômé — the final year of the student's CNPN is cleared. Distinct from
    /// <see cref="Validated"/> because there is no level above it to move to.
    /// </summary>
    Graduated,

    /// <summary>
    /// Exclu — the faculty ended the cursus (repeat limit, discipline). Distinct from
    /// <see cref="Failed"/>: one repeats the year, the other has no next year at all, and the
    /// réinscription step must tell them apart.
    /// </summary>
    Excluded,
}

public static class RegistrationStatusExtensions
{
    /// <summary>
    /// True for the verdicts a deliberation can pronounce. <see cref="RegistrationStatus.Pending"/>
    /// and <see cref="RegistrationStatus.Active"/> are positions in a year that is still running, so
    /// recording either as an outcome would close a year by re-opening it.
    /// </summary>
    public static bool IsYearOutcome(this RegistrationStatus status) =>
        status is RegistrationStatus.Validated
               or RegistrationStatus.Failed
               or RegistrationStatus.Withdrawn
               or RegistrationStatus.Graduated
               or RegistrationStatus.Excluded;

    /// <summary>
    /// True when the outcome ends the student's course of study, so no registration follows it.
    /// <see cref="RegistrationStatus.Graduated"/> ends it by success, the other two otherwise.
    /// </summary>
    public static bool EndsTheCursus(this RegistrationStatus status) =>
        status is RegistrationStatus.Graduated
               or RegistrationStatus.Excluded
               or RegistrationStatus.Withdrawn;
}
