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

    /// <summary>
    /// True when the year is <b>annulled</b>, so the stages served inside it establish nothing —
    /// neither an acquisition nor a debt. A redoublant repeats the year from scratch, including the
    /// stages he passed, so an attempt made in it is history and not a fact about what he owes.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>It is the year's verdict that annuls, not the stage's.</b> A stage failed in a year
    /// the student <i>cleared</i> is the ordinary carried credit — it stays owed and is settled by
    /// <c>RevalidateStageCommand</c>. Only <see cref="RegistrationStatus.Failed"/> wipes the slate,
    /// which is why this is not simply « the year did not go well »:
    /// <see cref="RegistrationStatus.Withdrawn"/> and <see cref="RegistrationStatus.Excluded"/> end
    /// the cursus rather than repeat the year, and nobody has ruled that what was served before an
    /// abandon never happened.</para>
    ///
    /// <para>⚠ <b>An unpronounced year annuls nothing.</b> <see cref="RegistrationStatus.Active"/> is
    /// what the legacy import wrote on every historical registration — no verdict was ever recorded
    /// for them — so reading « pas encore validée » as « annulée » would retroactively make the whole
    /// imported cursus outstanding. Silence is not a failure.</para>
    ///
    /// <para><b>The case it exists for.</b> A student passes Chirurgie, fails the year, repeats it and
    /// fails Chirurgie the second time. Without this he reads as having acquired the stage — on the
    /// strength of a year that was annulled — and <c>FinalYearGuard</c> lets him into his last year
    /// owing it. Read through here, the annulled attempt drops out, the surviving one is a failure,
    /// and he owes it.</para>
    /// </remarks>
    public static bool AnnulsItsStages(this RegistrationStatus status) =>
        status is RegistrationStatus.Failed;
}
