using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Moving a column that is already published, <b>with the périodes published from it</b> — phase 17.1.
/// </summary>
/// <remarks>
/// <para><b>Why it is one operation and not two.</b> Until this existed, moving a published column was
/// refused outright (<c>Schedule.SlotPublishedCannotMove</c>) because the two halves come apart in
/// silence: the créneau takes its new dates, the périodes published from it keep their old ones, and
/// no screen says which is true. Deleting such a column failed loudly; moving it succeeded and lied.
/// The only remedy on offer was « dépubliez d'abord », and on a promotion published in its entirety
/// that means destroying a year's plan to shift one week.</para>
///
/// <para>⚠ <b>A période's window is re-derived, never offset.</b> Under
/// <see cref="StageRotationMode.SingleService"/> one période spans a whole <i>run</i> of columns, so
/// « add the same delta » is wrong for every run whose moved column is not its only one. The span is
/// recomputed as min/max over the cells the période actually covers — the same rule
/// <see cref="CohortStayFolder"/> applies when publishing — so a move and a fresh publication cannot
/// disagree about where a stay begins. Moving a middle column of a run therefore leaves that run's
/// span untouched, which is the correct answer and not one an offset would have produced.</para>
///
/// <para>⚠ <b>What it refuses, and why those and not others.</b> A période that has <i>begun</i>, that
/// carries an evaluation, or that carries attendance is not moved: a mark and a day of presence are
/// facts about dates that already happened, and sliding the window under them makes the register lie.
/// Attendance is the silent half — a mark announces itself on every screen, a présence is invisible
/// until the day somebody needs it — so it is counted and named rather than left to the cascade. This
/// is the same bargain the affectation-sheet reversal makes, for the same reason.</para>
///
/// <para>⚠ <b>And the run must still run in order.</b> A column dragged past its neighbours would
/// leave a <see cref="StageRotationMode.SingleService"/> run whose columns no longer follow one
/// another in date order — a single stay with a hole in it, or with its parts out of sequence. The
/// période's span would still compute, and it would describe a continuous presence that never
/// happened.</para>
/// </remarks>
internal sealed class PublishedPeriodShifter(IApplicationDbContext dbContext)
{
    /// <summary>
    /// What moving <paramref name="slotId"/> to the given window would do to the périodes published
    /// from it — computed without writing anything, so the preview and the act cannot disagree.
    /// </summary>
    public async Task<Result<SlotMovePlan>> PlanAsync(
        int slotId, DateOnly newStart, DateOnly newEnd, DateOnly on, CancellationToken ct)
    {
        var cells = await CoveredCellsQuery(dbContext, slotId).ToListAsync(ct);

        if (cells.Count == 0)
            return SlotMovePlan.Empty;

        var periods = await AffectedPeriodsQuery(dbContext, slotId).ToListAsync(ct);

        var blocked = periods
            .Where(p => !ServicePeriodLifecycle.IsMovableOn(
                p.IsComplete, p.IsInterrupted, p.HasEvaluation, p.AttendanceCount > 0,
                p.StartDate, on))
            .ToList();

        if (blocked.Count > 0)
            return Result.Failure<SlotMovePlan>(StageErrors.SlotPeriodsAlreadyUnderway(
                blocked.Count,
                blocked.Count(p => p.HasEvaluation),
                blocked.Sum(p => p.AttendanceCount)));

        var windows = new List<PeriodWindow>(periods.Count);

        foreach (var covered in cells.GroupBy(c => c.ServicePeriodId))
        {
            // The moved column's cells take the window being proposed; every other column keeps the
            // one it has. That substitution is what lets this run before anything is written.
            var run = covered
                .Select(c => c.StageSlotId == slotId
                    ? c with { StartDate = newStart, EndDate = newEnd }
                    : c)
                .OrderBy(c => c.PeriodNumber)
                .ToList();

            for (int i = 1; i < run.Count; i++)
                if (run[i - 1].EndDate >= run[i].StartDate)
                    return Result.Failure<SlotMovePlan>(StageErrors.SlotMoveBreaksRun(
                        run[i - 1].PeriodNumber, run[i].PeriodNumber));

            windows.Add(new PeriodWindow(
                covered.Key, run[0].StartDate, run[^1].EndDate));
        }

        int spanChanged = windows.Count(w => Changed(w, periods));

        return new SlotMovePlan(
            windows,
            PeriodsAffected: windows.Count,
            PeriodsWhoseWindowChanges: spanChanged);
    }

    /// <summary>Writes the recomputed windows onto the périodes. The caller saves.</summary>
    /// <remarks>
    /// ⚠ <b>Par l'agrégat, jamais sur les périodes seules.</b> Charger des <c>ServicePeriod</c> à plat
    /// et leur écrire des dates marchait, et laissait deux choses derrière : l'invariant — une rotation
    /// commencée, notée ou pointée ne se déplace pas — reposait entièrement sur
    /// <see cref="PlanAsync"/>, donc sur la bonne volonté de l'appelant ; et l'acte ne levait
    /// <b>aucun</b> événement de domaine, alors qu'il réécrit la fenêtre de milliers de rotations d'un
    /// coup. <c>InternshipAssignment.Reschedule</c> porte les deux.
    ///
    /// <para>⚠ <b>Un refus ici est une incohérence, pas un cas d'usage</b> : le plan a déjà écarté ces
    /// périodes. Il est rendu plutôt qu'ignoré parce que le seul état qui puisse le produire est une
    /// période évaluée entre l'aperçu et l'application — exactement le cas que la double confirmation
    /// existe pour attraper — et l'acte est atomique, donc rendre le refus annule tout.</para>
    /// </remarks>
    public async Task<Result> ApplyAsync(SlotMovePlan plan, DateOnly on, CancellationToken ct)
    {
        if (plan.Windows.Count == 0)
            return Result.Success();

        var ids = plan.Windows.Select(w => w.ServicePeriodId).ToList();

        // Les agrégats qui portent ces périodes, avec de quoi juger : l'évaluation et les présences
        // sont ce que la garde lit, et une collection non incluse est indiscernable d'une collection
        // vide — le fournisseur en mémoire, lui, la recolle depuis le change tracker et ne verrait
        // jamais l'oubli.
        var assignments = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Evaluation)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Attendance)
            .Where(a => a.ServicePeriods.Any(p => ids.Contains(p.Id)))
            .ToListAsync(ct);

        foreach (var window in plan.Windows)
        {
            var owner = assignments.FirstOrDefault(
                a => a.ServicePeriods.Any(p => p.Id == window.ServicePeriodId));

            if (owner is null)
                return Result.Failure(StageErrors.PeriodNotFound(window.ServicePeriodId));

            var moved = owner.Reschedule(window.ServicePeriodId, window.StartDate, window.EndDate, on);
            if (moved.IsFailure)
                return moved;
        }

        return Result.Success();
    }

    private static bool Changed(PeriodWindow window, IReadOnlyCollection<AffectedPeriod> periods)
    {
        var current = periods.FirstOrDefault(p => p.Id == window.ServicePeriodId);
        return current is not null
            && (current.StartDate != window.StartDate || current.EndDate != window.EndDate);
    }

    /// <summary>
    /// Every cell covered by every période that touches this column — <b>including the cells of other
    /// columns</b>, because a run's span is min/max over all of them.
    /// </summary>
    /// <remarks>
    /// ⚠ Flat and top-level, keyed on the parent id, rather than a collection subquery inside a
    /// projection: that shape is the one Npgsql refuses, and it killed the macro plan once with the
    /// whole suite green. Named so <c>SqlTranslationTests</c> can compile it without a database.
    /// </remarks>
    internal static IQueryable<CoveredCell> CoveredCellsQuery(IApplicationDbContext dbContext, int slotId) =>
        dbContext.ServicePeriodSlotCoverage
            .Where(c => dbContext.ServicePeriodSlotCoverage.Any(
                touching => touching.ServicePeriodId == c.ServicePeriodId
                         && touching.CohortSlotAssignment.StageSlotId == slotId))
            .Select(c => new CoveredCell(
                c.ServicePeriodId,
                c.CohortSlotAssignment.StageSlotId,
                c.CohortSlotAssignment.StageSlot.PeriodNumber,
                c.CohortSlotAssignment.StageSlot.StartDate,
                c.CohortSlotAssignment.StageSlot.EndDate));

    internal static IQueryable<AffectedPeriod> AffectedPeriodsQuery(
        IApplicationDbContext dbContext, int slotId) =>
        dbContext.ServicePeriods
            .Where(p => p.SlotCoverage.Any(c => c.CohortSlotAssignment.StageSlotId == slotId))
            .Select(p => new AffectedPeriod(
                p.Id,
                p.StartDate,
                p.EndDate,
                p.IsStarted,
                p.IsInterrupted,
                p.IsComplete,
                p.Evaluation != null,
                p.Attendance.Count));

    internal sealed record CoveredCell(
        Guid ServicePeriodId, int StageSlotId, int PeriodNumber, DateOnly StartDate, DateOnly EndDate);

    internal sealed record AffectedPeriod(
        Guid Id, DateOnly StartDate, DateOnly EndDate,
        bool IsStarted, bool IsInterrupted, bool IsComplete, bool HasEvaluation, int AttendanceCount);
}

/// <param name="PeriodsWhoseWindowChanges">
/// ⚠ Distinct from <paramref name="PeriodsAffected"/>, and the difference is the whole of what makes
/// a <see cref="StageRotationMode.SingleService"/> move legible: moving a middle column of a run
/// touches périodes whose span does <b>not</b> move. One number for both would tell the operator that
/// hundreds of rotations are being rewritten when none of them is.
/// </param>
internal sealed record SlotMovePlan(
    IReadOnlyList<PeriodWindow> Windows,
    int PeriodsAffected,
    int PeriodsWhoseWindowChanges)
{
    public static readonly SlotMovePlan Empty = new([], 0, 0);
}

internal sealed record PeriodWindow(Guid ServicePeriodId, DateOnly StartDate, DateOnly EndDate);
