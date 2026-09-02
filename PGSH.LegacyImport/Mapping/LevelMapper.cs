using PGSH.Application.Stages.Levels;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;

namespace PGSH.LegacyImport.Mapping;

/// <summary>
/// Legacy `Niveaux.CodeN` → the PGSH <see cref="Level"/> it belongs to.
///
/// <para>⚠ <b>The table itself lives in <see cref="FacultyLevelCodes"/>, not here.</b> It used to be
/// this file's private dictionary, which was fine while the importer was the only thing that had to
/// read a faculty code. It is not any more: the faculty's own réinscription roll is keyed on the
/// same vocabulary, so a second copy would be two answers to « quel niveau est MDME3 ? » — a
/// promotion imported at one level and re-registered at another, with nothing able to notice.</para>
///
/// <para>Two pairs are the <em>same</em> level under two codes — the faculty renamed them, it did
/// not add a year: <c>MED01</c> ran 2015/16–2023/24 and <c>MDME1</c> took over in 2025/26 (likewise
/// <c>MED02</c>/<c>MDME2</c>, <c>MDPH01</c>/<c>MPHAR1</c>, <c>MDPH02</c>/<c>MPHAR2</c>). They
/// therefore map onto one <c>(Year, Program)</c>, which is also what the unique index demands.</para>
/// </summary>
public static class LevelMapper
{
    public static LevelKey? Resolve(string? codeN) =>
        FacultyLevelCodes.Resolve(codeN) is { } code
            ? new LevelKey(code.Year, code.Program, code.Label)
            : null;

    /// <summary>Every distinct level the codes resolve to — what the importer creates up front.</summary>
    /// <remarks>
    /// ⚠ This is deliberately every level <em>the vocabulary can name</em>, not every level the
    /// .mdb happens to use. <c>MDME3</c> and <c>MPHAR3</c> appear in the 2026-2027 réinscription roll
    /// and in no legacy row, and the roll is applied against the catalogue this creates.
    /// </remarks>
    public static IReadOnlyCollection<LevelKey> AllLevels() =>
        FacultyLevelCodes.DistinctLevels()
            .Select(c => new LevelKey(c.Year, c.Program, c.Label))
            .ToList();

    /// <summary>
    /// True when the code marks a withdrawal rather than a year of study.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>MED00</c> = « RETRAIT ». It is kept as a <c>Level</c> with <c>Year = 0</c> so the
    /// registration — and the rotations already served that year — survive the import rather than
    /// being dropped; the meaning lives in <c>Registration.Status = Withdrawn</c>, and
    /// <c>Level.IsPromotion</c> is what keeps it out of every planning picker. The real year the
    /// student withdrew from is <b>not recoverable</b>: the source overwrote it. Do not "repair" it.
    /// </remarks>
    public static bool IsWithdrawal(string? codeN) =>
        FacultyLevelCodes.Resolve(codeN) is { IsPromotion: false };
}

public sealed record LevelKey(int Year, AcademicProgram Program, string Label);
