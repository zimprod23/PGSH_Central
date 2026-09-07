using PGSH.Application.Abstractions.Data;
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
