using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.GetMany;

// RotationMode is carried here as well as on the detail response: the list row is what the admin
// scans to see how each stage runs, and a summary that omits a field the edit form writes back is
// how HospitalSummaryResponse silently erased every hospital description.
public sealed record StageSummaryResponse(
    int Id,
    string Name,
    int Coefficient,
    int DurationInDays,
    string LevelLabel,
    StageRotationMode RotationMode,
    IReadOnlyList<StageTextFigure> TextFigures);

/// <summary>
/// What one CNPN's requirement set states of this stage, beside what the catalogue states.
/// </summary>
/// <remarks>
/// <para><c>Stage.Coefficient</c> and <c>Stage.DurationInDays</c> are duplicated by every
/// <c>CurriculumStage</c> for the same stage, and they agreed only for as long as no text had
/// reweighted one — the reconstruction seeded one from the other. Arrêté 1650.25 is the first that
/// does: measured on the live base 2026-09-01, MED3 Chirurgie reads coefficient <b>3</b> in the
/// catalogue and <b>1</b> in 1650.25, <b>30</b> jours ouvrables in the catalogue and <b>66</b> in
/// 2174.18's set.</para>
/// <para>⚠ <b>Neither figure is wrong, and that is the point.</b> A 5ᵉ année student revalidating a
/// 3ᵉ année credit is still governed by 2174.18, which is why the alignment migration recorded 66
/// there *before* overwriting the catalogue. What was wrong is that the Stages page rendered the
/// catalogue number unqualified — a figure no CNPN necessarily states — with nothing on the screen
/// saying another text disagreed. The row now carries every text's own figures so the page can name
/// where each number comes from.</para>
/// <para>Bounded by the texts governing one stage's level (one or two in practice), per row of a
/// page that is already paginated. It is read by a <b>second flat query</b> keyed on the page's
/// stage ids, never as a collection inside the row projection — that element carries no key and is
/// the shape Npgsql refuses.</para>
/// </remarks>
public sealed record StageTextFigure(
    int CnpnVersionId,
    string CnpnCode,
    string LevelLabel,
    int Coefficient,
    int DurationInDays);
