using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;

namespace PGSH.Domain.Stages;

/// <summary>
/// « À partir de la 3ᵉ année de 2026-2027 » — one authored statement that a
/// <see cref="CnpnVersion"/> governs one <see cref="Level"/> from one academic year onward,
/// whoever is sitting in it.
///
/// <para><b>Why the entry criterion is not enough.</b> Arrêté 1650.25 art. 2 assigns by date of
/// first registration, and <c>CnpnAssignment</c> implements exactly that. But the faculty does not
/// always apply a text the way the text describes itself: after the 7→6 reduction was contested, the
/// cut actually applied was « la 3ᵉ année de 2026-2027 et en dessous » — a statement about the level
/// a student sits in during a given year, which deliberately catches the repeater who entered years
/// earlier and deliberately spares the student one year ahead of him. Two students with the same
/// entry year land on different texts, so no entry-based rule can express it.</para>
///
/// <para><b>It is read once per registration and then frozen.</b> The rule is consulted when a
/// <see cref="Registration"/> is created; the answer is written to
/// <see cref="Registration.CnpnVersionId"/> and never recomputed. That is what keeps both halves
/// true at once:</para>
/// <list type="bullet">
///   <item>the repeater re-registering in 3MED in 2026-2027 gets a <i>new</i> registration, so the
///   rule sees him and moves him — automatically, without anyone re-running a bulk command;</item>
///   <item>the student who has moved on to 4MED still owing two stages from his 3MED year is judged
///   under the text stamped on <i>that</i> registration, so reshaping 3MED cannot reach back and
///   change what he owes.</item>
/// </list>
///
/// <para>⚠ <b>This is not the live-state rule that <c>CnpnTargeting</c> deliberately avoids.</b> That
/// objection is about re-evaluating an existing student's stamp — « année ≤ 2 » selects different
/// people every September, and a student's text must not move under him. Evaluating once, at
/// creation, and freezing the answer preserves that guarantee exactly while removing the need for
/// somebody to remember to run the targeting command each year.</para>
///
/// <para><b>Scope is per level, and the absence of a row is meaningful.</b> « 3ᵉ année et en dessous »
/// is three rows (years 1, 2, 3), not a comparison stored on one. A student at a level with no row
/// keeps whatever text he already followed, which is precisely what "les 4ᵉ année restent sur
/// l'ancien texte" means — so the shape that expresses the cut is the same shape that expresses
/// « on ajoute des stages en 3ᵉ année », with one row instead of three.</para>
/// </summary>
/// <remarks>
/// ⚠ <b>Not an aggregate root: it is one sentence of a text.</b> Declare it through
/// <see cref="CnpnVersion.DeclareEffectivity"/> and withdraw it through
/// <see cref="CnpnVersion.WithdrawEffectivity"/>, which is where the rules about which levels a text
/// may speak for live. The <c>init</c> accessors exist so EF, the migration and the test seeds can
/// still materialise a row; they are not a second way in.
/// </remarks>
public sealed class CnpnLevelEffectivity
{
    public int Id { get; set; }

    public int CnpnVersionId { get; init; }
    public CnpnVersion CnpnVersion { get; init; } = default!;

    public int LevelId { get; init; }
    public Level Level { get; init; } = default!;

    /// <summary>
    /// The first academic year in which this text governs the level. Compared on
    /// <see cref="AcademicYear.StartDate"/>, so resolution is "the row for this level with the
    /// latest start date at or before the registration's year".
    /// </summary>
    public int FromAcademicYearId { get; init; }
    public AcademicYear FromAcademicYear { get; init; } = default!;

    /// <summary>
    /// Why the faculty drew the line here. Free text, and worth having: the cut is an administrative
    /// decision rather than a reading of the arrêté, so a year later nobody remembers whether « 3ᵉ
    /// année » was the ministry's wording or the outcome of a negotiation.
    /// </summary>
    public string? Note { get; init; }

    public DateTime RecordedOn { get; init; }
}
