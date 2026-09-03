namespace PGSH.Domain.Stages;

/// <summary>
/// Where one roster's planning cells stand relative to <b>one</b> hospital.
///
/// <para>The question « quel groupe est déjà au HMIMV ? » is the cheapest way to satisfy a placement
/// request — « cet étudiant fait tous ses stages à l'hôpital militaire » costs nothing at all when a
/// roster already goes there, because putting him in it is one transfer and pins nothing. Measured on
/// the imported history 2026-09-03: 2024-2025 6ᵉ année Médecine held <b>five</b> such rosters of 6-7
/// students each (groupes 102, 116, 130, 144, 158), so this is how the faculty already works.</para>
///
/// <para>⚠ <b><see cref="Unplaced"/> exists because an empty set satisfies « toutes ses cellules sont
/// au HMIMV » vacuously.</b> A roster nobody has arranged yet has no cell anywhere, and read as
/// <see cref="Entire"/> it would be offered as the military roster — the strongest possible answer
/// drawn from the complete absence of evidence. That is the same defect as an omitted academic year
/// read as « toutes les années » and a missing <c>Include</c> read as an empty collection: one state
/// standing in for another, and the wrong one is the reassuring one. It is also the <b>normal</b>
/// state of this base today, which holds 0 cells on every year.</para>
/// </summary>
public enum RosterHospitalPlacement
{
    /// <summary>The roster holds no planning cell at all — nothing has been arranged for it yet.</summary>
    Unplaced = 0,

    /// <summary>It is placed, and no cell of it is at this hospital.</summary>
    Elsewhere,

    /// <summary>Some of its cells are at this hospital and some are not.</summary>
    Partial,

    /// <summary>Every cell it holds is at this hospital — and it holds at least one.</summary>
    Entire,
}

/// <summary>
/// The one place <see cref="RosterHospitalPlacement"/> is decided. Pure — no store, no clock — like
/// <c>FinalYearTest</c> and <c>StageHospitalCoverageTest</c>, so the vacuous case can be stated
/// exactly rather than approximately seeded.
/// </summary>
public static class RosterHospitalPlacementTest
{
    /// <param name="cells">Every planning cell the roster holds in the scope being asked about.</param>
    /// <param name="cellsAtHospital">
    /// How many of those are in a service of the hospital. A subset count by construction, so it can
    /// never exceed <paramref name="cells"/>; the order of the tests below does not depend on it.
    /// </param>
    public static RosterHospitalPlacement Of(int cells, int cellsAtHospital) => cells switch
    {
        <= 0                             => RosterHospitalPlacement.Unplaced,
        _ when cellsAtHospital <= 0      => RosterHospitalPlacement.Elsewhere,
        _ when cellsAtHospital >= cells  => RosterHospitalPlacement.Entire,
        _                                => RosterHospitalPlacement.Partial,
    };
}
