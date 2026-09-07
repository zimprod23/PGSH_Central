using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;

namespace PGSH.Application.AcademicGroups;

/// <summary>
/// Which rosters a bulk act touches: a year's, or one promotion's inside it.
/// </summary>
/// <remarks>
/// <para>⚠ <b>The two bulk roster acts ask this identical question, and they used to answer it
/// separately.</b> « Vider » gained the promotion scope on 2026-09-07 and « Supprimer » did not, so
/// deleting a promotion's rosters was refused over <i>another</i> promotion's students and the only
/// way through was to empty the whole year. One predicate, stated once, is what stops the pair
/// drifting apart again.</para>
///
/// <para>⚠ <b>An omitted level means the whole year, and that is the *only* place absence may
/// widen here</b> — every caller resolves an unknown level to <c>Levels.NotFound</c> first, rather
/// than letting a bad id fall through to « no level named » on an act that writes year-wide.</para>
///
/// <para>⚠ <b>« Non réparti » carries a null <c>LevelId</c></b> — it holds every promotion's
/// unassigned registrations at once — so a promotion-scoped act steps over it and only the year-wide
/// one reaches it. That is the right split: it is not the named promotion's to empty or destroy.</para>
/// </remarks>
internal static class RosterScope
{
    /// <summary>
    /// Named rather than inlined so <c>SqlTranslationTests</c> can compile it without a database —
    /// <c>LevelId</c> is nullable, so the comparison is one EF lifts rather than translates plainly.
    /// </summary>
    internal static IQueryable<AcademicGroup> Query(
        IApplicationDbContext dbContext, int academicYearId, int? levelId) =>
        dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == academicYearId
                     && (levelId == null || g.LevelId == levelId));
}
