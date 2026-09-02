using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Levels;

/// <summary>
/// The faculty's own code for a year of study — <c>MED04</c>, <c>MDME3</c>, <c>MDPH06</c> — and which
/// PGSH <see cref="Level"/> it names.
///
/// <para><b>Why this is not a column on <see cref="Level"/>.</b> The mapping is many-to-one and has
/// been since 2025-2026: the faculty is renaming its codes one promotion at a time as each cohort
/// moves up, so <c>MED01</c> and <c>MDME1</c> are the <em>same</em> first year under two names, and
/// in 2026-2027 the third year is <c>MED03</c> for the students repeating it and <c>MDME3</c> for
/// the ones arriving. A single code column could not hold both, and the level a student sits in is
/// the same level either way — the rename is vocabulary, not structure.</para>
///
/// <para><b>An explicit table rather than parsing the string.</b> There are two dozen of them and
/// they are a closed set. Inferring a year from « the digits at the end » reads <c>MDME3</c> and
/// <c>MMBTM1</c> the same way, and the second is a master's degree PGSH does not manage at all.</para>
///
/// <para>⚠ <b>Codes PGSH knowingly does not manage are listed too</b> — see
/// <see cref="OutsideScope"/>. That is the whole reason this is a table: an importer needs to tell
/// « a programme we do not cover » from « a code nobody has told us about ». The first is skipped
/// and counted; the second refuses the file, because a mistyped code silently dropped is a student
/// who quietly does not get re-registered.</para>
///
/// <para>Pure — no store, no clock — like <c>EntryYearDeduction</c> and <c>PeriodAxis</c>, so the
/// cases are exact rather than approximately seeded. <c>LegacyImport.LevelMapper</c> reads this one
/// rather than carrying its own copy: two tables for one vocabulary is how a promotion ends up
/// imported at one level and re-registered at another.</para>
/// </summary>
public static class FacultyLevelCodes
{
    private static readonly Dictionary<string, FacultyLevelCode> Levels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Médecine. MED0n is the original code; MDMEn is the rename, and both are live at once
            // while the cohorts cross over.
            ["MED01"]  = new("MED01",  1, AcademicProgram.Medecine, "Première Année Médecine"),
            ["MDME1"]  = new("MDME1",  1, AcademicProgram.Medecine, "Première Année Médecine"),
            ["MED02"]  = new("MED02",  2, AcademicProgram.Medecine, "Deuxième Année Médecine"),
            ["MDME2"]  = new("MDME2",  2, AcademicProgram.Medecine, "Deuxième Année Médecine"),
            ["MED03"]  = new("MED03",  3, AcademicProgram.Medecine, "Troisième Année Médecine"),
            ["MDME3"]  = new("MDME3",  3, AcademicProgram.Medecine, "Troisième Année Médecine"),
            ["MED04"]  = new("MED04",  4, AcademicProgram.Medecine, "Quatrième Année Médecine"),
            ["MED05"]  = new("MED05",  5, AcademicProgram.Medecine, "Cinquième Année Médecine"),
            ["MED06"]  = new("MED06",  6, AcademicProgram.Medecine, "Sixième Année Médecine"),
            ["MED07"]  = new("MED07",  7, AcademicProgram.Medecine, "Septième Année Médecine"),
            ["INM"]    = new("INM",    8, AcademicProgram.Medecine, "Interne CHU Médecine"),

            // Pharmacie. Same rename, one year behind: MDPH0n → MPHARn.
            ["MDPH01"] = new("MDPH01", 1, AcademicProgram.Pharmacie, "Première Année Pharmacie"),
            ["MPHAR1"] = new("MPHAR1", 1, AcademicProgram.Pharmacie, "Première Année Pharmacie"),
            ["MDPH02"] = new("MDPH02", 2, AcademicProgram.Pharmacie, "Deuxième Année Pharmacie"),
            ["MPHAR2"] = new("MPHAR2", 2, AcademicProgram.Pharmacie, "Deuxième Année Pharmacie"),
            ["MDPH03"] = new("MDPH03", 3, AcademicProgram.Pharmacie, "Troisième Année Pharmacie"),
            ["MPHAR3"] = new("MPHAR3", 3, AcademicProgram.Pharmacie, "Troisième Année Pharmacie"),
            ["MDPH04"] = new("MDPH04", 4, AcademicProgram.Pharmacie, "Quatrième Année Pharmacie"),
            ["MDPH05"] = new("MDPH05", 5, AcademicProgram.Pharmacie, "Cinquième Année Pharmacie"),
            ["MDPH06"] = new("MDPH06", 6, AcademicProgram.Pharmacie, "Sixième Année Pharmacie"),
            ["INP"]    = new("INP",    9, AcademicProgram.Pharmacie, "Interne CHU Pharmacie"),

            // ⚠ « RETRAIT » — a withdrawal marker, not a year of study. Year 0, so `Level.IsPromotion`
            // is false and every planning path refuses it. The source *replaced* the real year with
            // this code, so the year the student withdrew from is not recoverable; do not "repair" it.
            ["MED00"]  = new("MED00",  0, AcademicProgram.Medecine, "Retrait"),
        };

    /// <summary>
    /// Codes the faculty uses for programmes PGSH does not manage, listed so an import can skip them
    /// deliberately instead of failing to recognise them.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The value is a description, not an assertion about the diploma.</b> These codes appear in
    /// the faculty's réinscription roll — 23 rows of <c>MMBTM1 → MMBTM2</c> in the 2026-2027 file —
    /// and PGSH holds no <c>Level</c>, no stage and no CNPN for them. Naming the degree would be a
    /// guess; naming the code and saying it is out of scope is what is actually known.
    /// </remarks>
    private static readonly Dictionary<string, string> OutOfScope =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MMBTM1"] = "Master « MMBTM » — 1ʳᵉ année (hors périmètre PGSH)",
            ["MMBTM2"] = "Master « MMBTM » — 2ᵉ année (hors périmètre PGSH)",
        };

    /// <summary>The level a code names, or <see langword="null"/> when it names none.</summary>
    public static FacultyLevelCode? Resolve(string? code) =>
        Normalize(code) is { } key && Levels.TryGetValue(key, out var level) ? level : null;

    /// <summary>
    /// A human description when the code belongs to a programme PGSH deliberately does not cover, or
    /// <see langword="null"/> — which, together with a null <see cref="Resolve"/>, means the code is
    /// simply unknown and somebody has to say which of the two it is.
    /// </summary>
    public static string? OutsideScope(string? code) =>
        Normalize(code) is { } key && OutOfScope.TryGetValue(key, out string? label) ? label : null;

    /// <summary>Every distinct <c>(year, programme)</c> the codes resolve to.</summary>
    public static IReadOnlyCollection<FacultyLevelCode> DistinctLevels() =>
        Levels.Values.DistinctBy(l => (l.Year, l.Program)).ToList();

    private static string? Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim();
}

/// <summary>
/// One faculty code and the level it names. <see cref="Code"/> is the first code recorded for that
/// level, not the only one — <c>MED01</c> and <c>MDME1</c> resolve to two records that agree on
/// <see cref="Year"/> and <see cref="Program"/>, which is what <c>IX_Level_Year_Program</c> keys on.
/// </summary>
public sealed record FacultyLevelCode(string Code, int Year, AcademicProgram Program, string Label)
{
    /// <summary>A year of study, as opposed to « Retrait ». Mirrors <see cref="Level.IsPromotion"/>.</summary>
    public bool IsPromotion => Year > 0;
}
