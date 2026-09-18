using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.DeleteAll;

/// <summary>
/// Removes the rosters of one year — or, when a promotion is named, that promotion's rosters only —
/// together with the cohortes hanging off them.
/// </summary>
/// <remarks>
/// <para>⚠ <b><paramref name="LevelId"/> is the scope the act is actually run at.</b> Every other
/// roster act is per promotion — the cut, the arrangement, the rotation block, and « Vider » since
/// 2026-09-07 — because rosters are keyed (année, promotion, numéro). Deleting was the last one
/// jumping straight to the whole year, so it refused over <i>other</i> promotions' students and the
/// only way through was to empty every promotion of the year. Reported 2026-09-07, in exactly those
/// words: « quand j'ai vidé tous les groupes de toutes les promotions, ça a marché ».</para>
///
/// <para>⚠ <b>Deleting is not the ordinary teardown step.</b> Emptied rosters refill, so a
/// re-découpage needs « Vider » and nothing more; this act exists to change their <b>number</b> or
/// their <b>numbering</b> — see <c>docs/planning-rosters.md</c>, « Repartir de zéro », step 5.</para>
///
/// <para>⚠ <b>« Non réparti » is not a promotion's roster.</b> It carries a null <c>LevelId</c>
/// because it holds every promotion's unassigned registrations at once, so a promotion-scoped delete
/// leaves it standing and only the year-wide act reaches it. That is the right split: it is not the
/// named promotion's to destroy.</para>
/// </remarks>
public sealed record DeleteAllGroupsCommand(int? AcademicYearId, int? LevelId = null)
    : ICommand<int>, IAuditableCommand
{
    // Two codes, for the reason EmptyAllYearGroupsCommand has two: a register that calls both acts
    // by one name cannot answer which of them was played.
    public string AuditAction => LevelId is null ? "YEAR_GROUPS_DELETED" : "PROMOTION_GROUPS_DELETED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId!.Value.ToString();

    public string? AuditMetadata => LevelId is null
        ? null
        : AuditMetadataJson.Of(("levelId", LevelId.Value));
}
