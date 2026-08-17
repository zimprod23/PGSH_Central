using PGSH.Domain.Common.Utils;

namespace PGSH.LegacyImport.Mapping;

/// <summary>
/// Legacy `Niveaux.CodeN` → the PGSH <see cref="Level"/> it belongs to.
///
/// An explicit table rather than parsing the code: there are only 20 of them, they are a closed set,
/// and inferring a year from a string would silently misfile a student the day someone adds a code.
///
/// Two pairs are the <em>same</em> level under two codes — the faculty renamed them, it did not add a
/// year: `MED01` ran 2015/16–2023/24 and `MDME1` took over in 2025/26 (likewise `MED02`/`MDME2`,
/// `MDPH01`/`MPHAR1`, `MDPH02`/`MPHAR2`). They therefore map onto one <c>(Year, Program)</c>, which is
/// also what the unique index demands.
/// </summary>
public static class LevelMapper
{
    private static readonly Dictionary<string, LevelKey> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Médecine
        ["MED01"]  = new(1, AcademicProgram.Medecine, "Première Année Médecine"),
        ["MDME1"]  = new(1, AcademicProgram.Medecine, "Première Année Médecine"),
        ["MED02"]  = new(2, AcademicProgram.Medecine, "Deuxième Année Médecine"),
        ["MDME2"]  = new(2, AcademicProgram.Medecine, "Deuxième Année Médecine"),
        ["MED03"]  = new(3, AcademicProgram.Medecine, "Troisième Année Médecine"),
        ["MED04"]  = new(4, AcademicProgram.Medecine, "Quatrième Année Médecine"),
        ["MED05"]  = new(5, AcademicProgram.Medecine, "Cinquième Année Médecine"),
        ["MED06"]  = new(6, AcademicProgram.Medecine, "Sixième Année Médecine"),
        ["MED07"]  = new(7, AcademicProgram.Medecine, "Septième Année Médecine"),
        ["INM"]    = new(8, AcademicProgram.Medecine, "Interne CHU Médecine"),

        // Pharmacie
        ["MDPH01"] = new(1, AcademicProgram.Pharmacie, "Première Année Pharmacie"),
        ["MPHAR1"] = new(1, AcademicProgram.Pharmacie, "Première Année Pharmacie"),
        ["MDPH02"] = new(2, AcademicProgram.Pharmacie, "Deuxième Année Pharmacie"),
        ["MPHAR2"] = new(2, AcademicProgram.Pharmacie, "Deuxième Année Pharmacie"),
        ["MDPH03"] = new(3, AcademicProgram.Pharmacie, "Troisième Année Pharmacie"),
        ["MDPH04"] = new(4, AcademicProgram.Pharmacie, "Quatrième Année Pharmacie"),
        ["MDPH05"] = new(5, AcademicProgram.Pharmacie, "Cinquième Année Pharmacie"),
        ["MDPH06"] = new(6, AcademicProgram.Pharmacie, "Sixième Année Pharmacie"),
        ["INP"]    = new(9, AcademicProgram.Pharmacie, "Interne CHU Pharmacie"),

        // "RETRAIT" — a withdrawal marker, not a year of study. Year 0 is inside Level's 0–10 range,
        // so the registration keeps a real level rather than being dropped; its status carries the
        // actual meaning. 13 registrations, 8 of which still have rotations on record.
        //
        // ⚠ Because it is a Level, every path that treats a level as a promotion will offer it: it was
        // selectable in the planning pickers, and one of its rosters ended up carrying a partition
        // label (an artefact of SplitAcademicGroupsPerLevel copying the parent roster's label onto
        // each shard). `Level.IsPromotion` is the single test for this, and the assign/auto-arrange
        // commands refuse a non-promotion. Do not "fix" the data: MED00 *replaced* the real year in
        // the source, so the year the student withdrew from is not recoverable.
        ["MED00"]  = new(0, AcademicProgram.Medecine, "Retrait"),
    };

    public static LevelKey? Resolve(string? codeN) =>
        codeN is not null && Map.TryGetValue(codeN.Trim(), out var key) ? key : null;

    /// <summary>Every distinct level the codes resolve to — what the importer creates up front.</summary>
    public static IReadOnlyCollection<LevelKey> AllLevels() =>
        Map.Values.DistinctBy(v => (v.Year, v.Program)).ToList();

    /// <summary>True when the code marks a withdrawal rather than a year of study.</summary>
    public static bool IsWithdrawal(string? codeN) =>
        string.Equals(codeN?.Trim(), "MED00", StringComparison.OrdinalIgnoreCase);
}

public sealed record LevelKey(int Year, AcademicProgram Program, string Label);
