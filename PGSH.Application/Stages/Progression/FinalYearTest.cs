namespace PGSH.Application.Stages.Progression;

/// <summary>
/// « Est-ce que cette année peut être sa dernière ? » — asked per <b>student</b>, from his own text.
///
/// <para>From 2026-2027 a 6ᵉ année Médecine holds students whose text ends there (arrêté 1650.25,
/// six years) beside students who go on to a 7ᵉ (arrêté 2174.18, seven), so the level alone never
/// answers it. Below every text's final year the answer is the same whichever text applies, which is
/// why a student nobody has stamped can still be handled safely.</para>
///
/// <para><b>It is a question, not a guard.</b> <see cref="FinalYearGuard"/> decides whether somebody
/// may <em>enter</em> a final year; this only says whether a year <em>might be</em> one, and every
/// caller uses that to stand aside rather than to refuse:</para>
/// <list type="bullet">
///   <item>the déliberation's default promotes but never graduates — in a final year, lingering is as
///   ordinary as finishing (855 of 1 657 students in 7ᵉ année Médecine had been there before), so
///   silence cannot be read as a verdict;</item>
///   <item>the réinscription roll records « redoublant » for a student re-entering the level he was
///   in, <em>except</em> in a final year, which is not a year one passes or fails: there is no
///   déliberation for it, the student validates and revalidates his stages one at a time and sits the
///   examens cliniques once they are done, and he is re-registered until both are cleared. Recording
///   a failure there would annul the year's stages.</item>
/// </list>
///
/// <para>Pure — no store, no clock — like <c>EntryYearDeduction</c>, <c>PeriodAxis</c> and
/// <c>StagePeriodFolder</c>. Written once because two copies of it would disagree about 804 students
/// in the 2026-2027 roll alone.</para>
/// </summary>
internal static class FinalYearTest
{
    /// <summary>
    /// Whether <paramref name="levelYear"/> may be the last year of this student's cursus.
    /// </summary>
    /// <param name="levelYear">The year of the level in question.</param>
    /// <param name="totalYears">
    /// How long his own text runs, or <see langword="null"/> when no text is on record.
    /// ⚠ Callers must pass <see langword="null"/> rather than <c>0</c> — a dictionary of
    /// <c>int</c> read with <c>GetValueOrDefault</c> yields 0, and a cursus « running 0 years » makes
    /// <em>every</em> year the last one, which fires hardest on exactly the students it must stand
    /// aside for.
    /// </param>
    /// <param name="earliestFinalYearOfProgramme">
    /// The shortest text of his programme, used only when he carries none of his own. Erring towards
    /// « peut-être » is the safe direction: every caller responds to a yes by writing nothing.
    /// </param>
    internal static bool MayBeFinal(
        int levelYear, int? totalYears, int? earliestFinalYearOfProgramme) =>
        totalYears is { } total
            ? total > 0 && levelYear >= total
            : earliestFinalYearOfProgramme is { } earliest && earliest > 0 && levelYear >= earliest;

    /// <summary>
    /// Whether <paramref name="levelYear"/> <b>is</b> the last year of this student's cursus —
    /// exactly, and only where his own text says so.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Stricter than <see cref="MayBeFinal"/> in two ways, and both are deliberate.</b>
    /// It compares with <c>==</c> rather than <c>&gt;=</c>, and it refuses to answer at all without a
    /// text. That is because its callers write « Diplômé », which <em>ends a cursus</em> — where
    /// <see cref="MayBeFinal"/>'s callers respond to a yes by writing nothing.</para>
    ///
    /// <para>The <c>==</c> is what keeps a registration sitting <em>above</em> its text's span out of
    /// it: measured 2026-08-29 the base holds 6 (5 in 7ᵉ année Médecine stamped <c>PHARM-LEGACY</c>,
    /// which runs 6 years, and 1 in « Interne CHU »). Such a row can neither be promoted by silence
    /// nor graduated — it is a data question, not a verdict, and the déliberation refuses it by name
    /// for the same reason (<c>NotAFinalYear</c>).</para>
    ///
    /// <para>⚠ <b>The missing-text case is where this and the déliberation part company, and the
    /// difference is who spoke.</b> The déliberation stands aside without a stamp and lets « Diplômé »
    /// through, because the faculty <em>named</em> that student on a document. Graduating from an
    /// <em>absence</em> names nobody, so a student PGSH holds no text for is left untouched and
    /// reported rather than graduated on a fallback.</para>
    /// </remarks>
    internal static bool IsExactlyFinal(int levelYear, int? totalYears) =>
        totalYears is { } total && total > 0 && levelYear == total;
}
