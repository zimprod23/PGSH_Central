using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;

namespace PGSH.Domain.Stages;

/// <summary>
/// One issue of the Cahier des Normes Pédagogiques Nationales — a ministerial text governing a whole
/// programme: how many years it lasts and, through its <see cref="Curriculum"/> entries, what each
/// level requires.
///
/// <para><b>Why this exists.</b> A CNPN does not apply to an academic year — it applies to a
/// <i>cohort</i>, and it follows that cohort until they graduate. Arrêté 1650.25 (BO 7422,
/// 17 July 2025) took the Médecine doctorate from seven years to six with effect from 2024-2025, but
/// article 2 leaves every student registered before that year under the previous text. From
/// 2026-2027 onward a single (level, year) cell therefore holds students of both texts — the ones
/// arriving on schedule under the new CNPN, and the ones repeating under the old. 2,635 students in
/// the imported history have repeated a level, so this is routine, not hypothetical. Keying the
/// requirement set on (level, academic year) cannot express it; keying it on (version, level) can.
/// </para>
///
/// <para><b>Assignment is by entry, and sticky.</b> <see cref="AppliesToEntrantsFromAcademicYearId"/>
/// is matched against the student's <i>first</i> registration, never against the current year, and
/// the resulting stamp does not move afterwards however long the student takes. See
/// <c>CnpnAssignment</c> in the application layer.</para>
///
/// <para><b>Not every version assigns anyone.</b> A text can be superseded without ever having
/// governed an intake — arrêté 2175.22 (2022) amended 2174.18 and was then explicitly disapplied by
/// 1650.25, which sends pre-2024-2025 students back to 2174.18 in its <i>pre-amendment</i> form.
/// Leave <see cref="AppliesToEntrantsFromAcademicYearId"/> null for such a version: it is recorded
/// for history and never selected for a new entrant.</para>
/// </summary>
public sealed class CnpnVersion
{
    public int Id { get; set; }

    /// <summary>The arrêté number, as the text is cited — e.g. "1650.25", "2174.18".</summary>
    public string Code { get; set; } = default!;

    /// <summary>Human label for the pickers — e.g. "CNPN 2025 — Docteur en Médecine (6 ans)".</summary>
    public string Label { get; set; } = default!;

    public AcademicProgram AcademicProgram { get; set; }

    /// <summary>
    /// How many years the programme lasts under this text: 7 for arrêté 2174.18, 6 for 1650.25. This
    /// is what makes "a 6-year student has no 7th year" something the application can actually know.
    /// </summary>
    public int TotalYears { get; set; }

    /// <summary>Publication reference — Bulletin Officiel number and date.</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// The first intake this text governs. A student is assigned the version with the greatest
    /// <see cref="AppliesToEntrantsFromAcademicYearId"/> at or before their entry year. Null means the
    /// version never governed an intake and is kept for history only.
    /// </summary>
    public int? AppliesToEntrantsFromAcademicYearId { get; set; }
    public AcademicYear? AppliesToEntrantsFromAcademicYear { get; set; }

    public ICollection<Curriculum> Curricula { get; set; } = new List<Curriculum>();

    /// <summary>
    /// The levels this text takes over from a given year onward, whoever is sitting in them — the
    /// second half of "who does this text bind", alongside
    /// <see cref="AppliesToEntrantsFromAcademicYearId"/>. Entry governs the new intake; these govern
    /// the promotions already in the building. See <see cref="CnpnLevelEffectivity"/>.
    /// </summary>
    public ICollection<CnpnLevelEffectivity> LevelEffectivities { get; set; } = new List<CnpnLevelEffectivity>();
}
