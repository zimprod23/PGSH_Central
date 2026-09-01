using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Empty;

/// <summary>
/// Takes every student out of every roster of one year.
/// </summary>
/// <remarks>
/// ⚠ <b>There is deliberately no <c>DropAffectations</c> here.</b> Its single-roster twin has one,
/// because a roster's affectations are a handful of rows an admin can be shown a count of and consent
/// to. A year's are the whole faculty's planning — 8 000 affectations and 100 000 périodes on this
/// base — and destroying them is not something anybody means by « retirer les étudiants des groupes ».
/// That act exists, per stage, where its cost is announced stage by stage:
/// <c>DeleteAllCohortsCommand</c>.
/// </remarks>
public sealed record EmptyAllYearGroupsCommand(int AcademicYearId) : ICommand<int>;
