using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Empty;

/// <summary>
/// Takes every student out of the rosters of one year — or, when a promotion is named, out of that
/// promotion's rosters only.
/// </summary>
/// <remarks>
/// <para>⚠ <b><paramref name="LevelId"/> is what a re-découpage actually needs.</b> Rosters are
/// keyed (année, promotion, numéro) and every act around them — the cut
/// (<c>AssignRotationGroupsCommand</c>), the arrangement (<c>AutoArrangeGroupsCommand</c>), the
/// rotation block — works on one promotion. Emptying jumped straight from one roster to the whole
/// year, so redoing the groups of a promotion whose planning had been cleared was refused over three
/// <i>other</i> promotions that were fully planned: 11 916 affectations and 11 407 périodes on
/// 2026-2027 for a 4ᵉ année Médecine that held none of them.</para>
///
/// <para>⚠ <b>There is deliberately no <c>DropAffectations</c> here</b>, at either scope. Its
/// single-roster twin has one, because a roster's affectations are a handful of rows an admin can be
/// shown a count of and consent to. A promotion's are its whole planning and a year's are the
/// faculty's — destroying them is not something anybody means by « retirer les étudiants des
/// groupes ». That act exists, per stage, where its cost is announced stage by stage:
/// <c>DeleteAllCohortsCommand</c>.</para>
/// </remarks>
public sealed record EmptyAllYearGroupsCommand(int AcademicYearId, int? LevelId = null)
    : ICommand<int>, IAuditableCommand
{
    // Two codes rather than one, because the two acts are not the same size and an audit register
    // that calls them both « YEAR_GROUPS_EMPTIED » cannot answer which one was run.
    public string AuditAction => LevelId is null ? "YEAR_GROUPS_EMPTIED" : "PROMOTION_GROUPS_EMPTIED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId.ToString();

    // The year is already on the entry as EntityId, so at year scope there is nothing further to say
    // and `{}` would claim otherwise. The promotion is not, and it is the whole difference.
    public string? AuditMetadata => LevelId is null
        ? null
        : AuditMetadataJson.Of(("levelId", LevelId.Value));
}
