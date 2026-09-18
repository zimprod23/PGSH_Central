using System.Linq.Expressions;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Extensions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// What a promotion holds across a span of dates: its créneaux, the cells hanging off them, and the
/// rotations already published from them.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Every one of these is scoped by (année, niveau) and never by either alone.</b> A stage id
/// spans every promotion that ever ran it, and a level exists in every year — the pair is the promotion,
/// and half of it is a different question rather than a wider answer.</para>
///
/// <para>⚠ <b>Named, and flat.</b> Named so <c>SqlTranslationTests</c> can compile them without a
/// database; flat — keyed on the parent id and folded in memory — because a collection subquery inside
/// a projection is the shape Npgsql refuses, and that is invisible to the in-memory suite.</para>
/// </remarks>
internal static class PromotionPauseQueries
{
    /// <summary>
    /// The rotations a « Démarrer » over this selection would actually start.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>This is a copy of <c>StagePeriodRunner.StartStageAsync</c>'s scoping, and the copy is
    /// the risk.</b> Cohortes take precedence over partition labels; the period-number filter reaches
    /// the column <em>through the grid cell</em>, so a période with no cell is out of scope when period
    /// numbers are named — exactly as the runner's in-memory filter does, because a période nobody can
    /// place in a column is one the button will skip. A report drawn from a different selection than the
    /// act is worse than none, since it is believed.</para>
    ///
    /// <para>⚠ <b>« About to start » means not yet started</b>, on an assignment that is
    /// <c>Planned</c> or <c>Ongoing</c> — the runner's own status filter. A rotation already under way
    /// is not what the button is about to do, and counting it would make the warning grow every time it
    /// was read.</para>
    /// </remarks>
    internal static IQueryable<ServicePeriod> PeriodsAboutToStartQuery(
        IApplicationDbContext dbContext,
        int stageId,
        int academicYearId,
        IReadOnlyList<int>? cohortIds,
        IReadOnlyList<string>? partitionLabels,
        IReadOnlyList<int>? periodNumbers)
    {
        var query = dbContext.ServicePeriods
            .Where(p => p.InternshipAssignment.Cohort.StageId == stageId)
            .Where(p => p.InternshipAssignment.Cohort.AcademicGroup.AcademicYearId == academicYearId)
            .Where(p => p.InternshipAssignment.Status == InternshipStatus.Planned
                     || p.InternshipAssignment.Status == InternshipStatus.Ongoing)
            .Where(p => !p.IsStarted);

        if (cohortIds is { Count: > 0 })
            query = query.Where(p => cohortIds.Contains(p.InternshipAssignment.CurrentCohortId));
        else if (partitionLabels is { Count: > 0 })
            query = query.Where(p =>
                p.InternshipAssignment.Cohort.AcademicGroup.RotationGroup != null
                && partitionLabels.Contains(p.InternshipAssignment.Cohort.AcademicGroup.RotationGroup));

        if (periodNumbers is { Count: > 0 })
            query = query.Where(p => p.CohortSlotAssignment != null
                                  && periodNumbers.Contains(p.CohortSlotAssignment.StageSlot.PeriodNumber));

        return query;
    }

    /// <summary>
    /// Of those, the ones crossing at least one window declared for <b>their own</b> promotion.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>A correlated <c>EXISTS</c> against the table, never a fold over a list of windows
    /// loaded first.</b> Two reasons. A rotation is months long and easily spans <em>several</em>
    /// windows, so summing per-window counts reports more rotations affected than the selection holds —
    /// the same double-count <c>ServiceOccupancyLookup.LoadOn</c> made with capacity, reached through a
    /// different door. And the correlation is on <c>Registration.LevelId</c>, so each période is matched
    /// against its own promotion's windows rather than against one promotion's windows applied to
    /// everybody.</para>
    /// </remarks>
    internal static IQueryable<ServicePeriod> CrossingADeclaredWindowQuery(
        IApplicationDbContext dbContext, IQueryable<ServicePeriod> periods, int academicYearId) =>
        periods.Where(p => dbContext.PromotionPauses.Any(w =>
            w.AcademicYearId == academicYearId
            && w.LevelId == p.InternshipAssignment.Registration.LevelId
            && p.StartDate <= w.EndDate
            && p.EndDate >= w.StartDate));

    internal sealed record PauseSpanRow(int PauseId, int SlotsSpanning, int PeriodsSpanning);

    /// <summary>
    /// For every window of an academic year, how much of the plan it crosses — columns and rotations —
    /// asked as <b>one</b> query so a page of windows is not a page of round-trips.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Two correlated <c>Count</c>s in the projection, which is the shape that <i>is</i>
    /// allowed.</b> What Npgsql refuses is a <em>collection</em> subquery in a projection — an element
    /// carrying no key it can correlate. A scalar count correlated on the outer row translates to a SQL
    /// sub-select and runs on the server, which is the point: the alternative is loading every période
    /// of the promotion to count it here.</para>
    ///
    /// <para>⚠ <b>The rotations are scoped through the <em>registration</em>, exactly as
    /// <see cref="PeriodsQuery"/> does</b>, and the two must not drift: a sixth-year re-taking a
    /// third-year stage sits his own promotion's exams, not the stage's. Scoping through
    /// <c>Cohort.Stage.LevelId</c> here and through the registration there would make the list and the
    /// preview report different numbers for one window, which is the one thing this file exists to
    /// prevent.</para>
    ///
    /// <para>⚠ <b>Zero has two benign readings and one that matters</b>, so the caller must not render
    /// it as an alarm: no column crosses the window either because the axis was laid <i>after</i> it
    /// was declared — the good order, and the whole point — or because the promotion has no axis yet.
    /// A non-zero count is the one that says a plan already laid is being cut.</para>
    /// </remarks>
    internal static IQueryable<PauseSpanRow> PauseSpansQuery(
        IApplicationDbContext dbContext, int academicYearId, int? levelId) =>
        dbContext.PromotionPauses
            .Where(p => p.AcademicYearId == academicYearId)
            .Where(p => levelId == null || p.LevelId == levelId)
            .Select(p => new PauseSpanRow(
                p.Id,
                dbContext.StageSlots.Count(s =>
                    s.AcademicYearId == p.AcademicYearId
                    && s.Stage.LevelId == p.LevelId
                    && s.StartDate <= p.EndDate
                    && s.EndDate >= p.StartDate),
                dbContext.ServicePeriods.Count(sp =>
                    sp.InternshipAssignment.Registration.AcademicYearId == p.AcademicYearId
                    && sp.InternshipAssignment.Registration.LevelId == p.LevelId
                    && sp.StartDate <= p.EndDate
                    && sp.EndDate >= p.StartDate)));

    internal sealed record SlotRow(
        int SlotId,
        int StageId,
        string StageName,
        int StatedDurationInDays,
        int PeriodNumber,
        string? Label,
        DateOnly StartDate,
        DateOnly EndDate);

    /// <summary>The promotion's columns overlapping <paramref name="from"/>…<paramref name="to"/>.</summary>
    internal static IQueryable<SlotRow> SlotsQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId, DateOnly from, DateOnly to) =>
        dbContext.StageSlots
            .Where(s => s.AcademicYearId == academicYearId && s.Stage.LevelId == levelId)
            .Where(s => s.StartDate <= to && s.EndDate >= from)
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.StageId)
            .ThenBy(s => s.PeriodNumber)
            .Select(s => new SlotRow(
                s.Id, s.StageId, s.Stage.Name, s.Stage.DurationInDays, s.PeriodNumber, s.Label,
                s.StartDate, s.EndDate));

    internal sealed record CellRow(int SlotId, int CohortId);

    /// <summary>
    /// One row per planning cell on those columns. Bounded by (columns crossed × cohortes of the
    /// promotion) — a few hundred — and folded per créneau in memory rather than asked as a sub-count
    /// inside <see cref="SlotsQuery"/>.
    /// </summary>
    internal static IQueryable<CellRow> CellsQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId, DateOnly from, DateOnly to) =>
        dbContext.CohortSlotAssignments
            .Where(c => c.StageSlot.AcademicYearId == academicYearId
                     && c.StageSlot.Stage.LevelId == levelId)
            .Where(c => c.StageSlot.StartDate <= to && c.StageSlot.EndDate >= from)
            .Select(c => new CellRow(c.StageSlotId, c.CohortId));

    /// <summary>
    /// The promotion's grid cells that a période was actually published from — the number that decides
    /// whether the axis can be <b>re-laid at all</b>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Not scoped to the window, deliberately.</b> <c>ApplyRotationCycleCommand</c> refuses on
    /// <c>PublishedCells &gt; 0</c> for the <em>whole</em> block, so one published cell anywhere in the
    /// promotion's year makes re-laying impossible — including for a column the window never touches.
    /// Telling the operator to re-lay in that state is telling them to click a button that refuses.
    ///
    /// <para>⚠ Through the coverage table, never through <c>ServicePeriod.CohortSlotAssignmentId</c>:
    /// that FK names the <i>first</i> cell of a run, so under <c>SingleService</c> the trailing cells of
    /// a published run would read as free. Same reasoning as <c>RotationCycleContext</c>'s own count.</para>
    /// </remarks>
    internal static IQueryable<int> PublishedCellsQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId) =>
        dbContext.ServicePeriodSlotCoverage
            .Where(cov => cov.CohortSlotAssignment.StageSlot.AcademicYearId == academicYearId
                       && cov.CohortSlotAssignment.StageSlot.Stage.LevelId == levelId)
            .Select(cov => cov.CohortSlotAssignmentId)
            .Distinct();

    /// <summary>
    /// The crossed columns a move would <b>refuse</b> — those carrying at least one rotation that has
    /// begun, been marked or been pointed. The crossed columns minus these are the ones the shortfall
    /// can actually be repaired on.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Asked of the columns the window crosses, not of the whole promotion</b> — the
    /// opposite scoping from <see cref="PublishedCellsQuery"/>, and for the opposite reason. Re-laying
    /// is all-or-nothing over the year, so one published cell anywhere refuses it; a move is one
    /// column, so a column whose neighbour is under way is still movable.</para>
    ///
    /// <para>⚠ <b>A période is reached through every cell it covers, which is what makes this
    /// agree with the act.</b> Under <c>StageRotationMode.SingleService</c> one rotation spans a run
    /// of columns, so a rotation that has begun blocks <em>each</em> column of its run —
    /// <c>PublishedPeriodShifter.AffectedPeriodsQuery</c> finds it from any of them. Reading
    /// <c>ServicePeriod.CohortSlotAssignmentId</c> instead would name only the run's first cell and
    /// report the trailing columns as movable when the act refuses them.</para>
    ///
    /// <para>⚠ The predicate is <c>ServicePeriodLifecycle.Movable</c> negated, never a fourth copy of
    /// the four flags: a report that promises a move the aggregate then refuses is worse than no
    /// report.</para>
    /// </remarks>
    internal static IQueryable<int> UnmovableSlotsQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId, DateOnly from, DateOnly to) =>
        dbContext.ServicePeriodSlotCoverage
            .Where(cov => cov.CohortSlotAssignment.StageSlot.AcademicYearId == academicYearId
                       && cov.CohortSlotAssignment.StageSlot.Stage.LevelId == levelId)
            .Where(cov => cov.CohortSlotAssignment.StageSlot.StartDate <= to
                       && cov.CohortSlotAssignment.StageSlot.EndDate >= from)
            .Where(CoversAnUnmovablePeriod)
            .Select(cov => cov.CohortSlotAssignment.StageSlotId)
            .Distinct();

    private static readonly Expression<Func<ServicePeriodSlotCoverage, ServicePeriod>> ToPeriod =
        coverage => coverage.ServicePeriod;

    /// <summary>
    /// « Cette ligne de couverture porte une rotation que le déplacement refuserait. » Recousue depuis
    /// <see cref="ServicePeriodLifecycle.Movable"/> par substitution du chemin — un <c>Invoke</c>
    /// serait refusé par EF — plutôt que réécrite, pour que le rapport et la garde ne puissent pas
    /// diverger.
    /// </summary>
    private static readonly Expression<Func<ServicePeriodSlotCoverage, bool>> CoversAnUnmovablePeriod =
        ToPeriod.Through(ServicePeriodLifecycle.Movable).Not();

    /// <summary>
    /// The promotion's rotations overlapping the span — counted, never listed: a promotion is ~900
    /// students and each holds one rotation per column.
    /// </summary>
    /// <remarks>
    /// ⚠ Scoped through the <b>registration</b>, which is where the promotion is recorded, rather than
    /// through <c>Cohort.Stage.LevelId</c>. A sixth-year student re-taking a third-year stage sits his
    /// own promotion's exams, not the stage's.
    /// </remarks>
    internal static IQueryable<ServicePeriod> PeriodsQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId, DateOnly from, DateOnly to) =>
        dbContext.ServicePeriods
            .Where(p => p.InternshipAssignment.Registration.AcademicYearId == academicYearId
                     && p.InternshipAssignment.Registration.LevelId == levelId)
            .Where(p => p.StartDate <= to && p.EndDate >= from);
}
