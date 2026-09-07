using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Calendar;
using PGSH.Domain.Stages;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// What a window costs the promotion it covers — the one place the answer is worked out, so the
/// preview, the declaration, the correction and the revocation cannot report different numbers for the
/// same dates.
/// </summary>
/// <remarks>
/// <para>The measurement is a difference between two calendars: the promotion's as it stands
/// <em>without</em> this window, and the same calendar <see cref="WorkingDayCalendar.With"/> it. ⚠
/// Counting on a calendar that already contains the window gives zero for every window ever declared —
/// the trap <c>HolidayResponse.WorkingDaysLost</c> avoids the same way.</para>
///
/// <para>⚠ <b>Nothing here writes, and neither does declaring.</b> A window is a calendar fact: the
/// créneaux and the périodes keep the dates they were given, and what this reader reports is the
/// shortfall that leaves behind.</para>
///
/// <para>⚠ <b>Whether that shortfall can be repaired is itself reported</b>, because it depends on the
/// promotion and not on the window. Re-laying the axis is a real act only while nothing has been
/// published from it — <c>ApplyRotationCycleCommand</c> refuses on <c>PublishedCells &gt; 0</c>, and the
/// 3ᵉ MED holds 804. Prescribing « reposez l'axe » in that state is prescribing a button that refuses,
/// which is worse than prescribing nothing; the warning therefore branches on
/// <see cref="PromotionPauseQueries.PublishedCellsQuery"/>.</para>
/// </remarks>
internal sealed class PromotionPauseImpactReader(
    IApplicationDbContext dbContext,
    WorkingDayProvider workingDays)
{
    /// <summary>
    /// How many créneaux the detail list carries. The true count is reported whatever this is — a
    /// number derived from a page is a number that quietly shrinks.
    /// </summary>
    internal const int MaxSlotRows = 200;

    /// <summary>
    /// The counts a confirmation names: how much of the plan a span touches. Cheap enough to ask on
    /// the write paths, where the full impact would be a second preview nobody asked for.
    /// </summary>
    public async Task<PromotionPauseSpan> SpanAsync(
        int academicYearId, int levelId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        int slots = await PromotionPauseQueries
            .SlotsQuery(dbContext, academicYearId, levelId, from, to)
            .CountAsync(cancellationToken);

        var periods = PromotionPauseQueries.PeriodsQuery(dbContext, academicYearId, levelId, from, to);

        return new PromotionPauseSpan(
            slots,
            await periods.CountAsync(cancellationToken),
            await periods.Where(ServicePeriodLifecycle.Underway).CountAsync(cancellationToken));
    }

    /// <summary>
    /// The full report: what the window takes, column by column and stage by stage, and how much of the
    /// promotion is standing in a service while it runs.
    /// </summary>
    /// <param name="excludingPauseId">
    /// The window already recorded whose replacement is being measured, so a correction is previewed
    /// against a calendar that does not still contain the old dates.
    /// </param>
    public async Task<PromotionPauseImpactResponse> MeasureAsync(
        Promotion promotion,
        DateOnly startDate,
        DateOnly endDate,
        string reason,
        bool isConfirmed,
        int? excludingPauseId,
        CancellationToken cancellationToken)
    {
        var (year, level, levelLabel) = promotion;
        int academicYearId = year.Id;
        int levelId = level.Id;

        var before = await workingDays.ForPromotionAsync(
            academicYearId, levelId, cancellationToken, excludingPauseId);

        var after = before.With(new ProposedClosure(
            startDate, endDate, reason, isConfirmed, CalendarClosureScope.Promotion));

        var slots = await PromotionPauseQueries
            .SlotsQuery(dbContext, academicYearId, levelId, startDate, endDate)
            .ToListAsync(cancellationToken);

        var cells = await PromotionPauseQueries
            .CellsQuery(dbContext, academicYearId, levelId, startDate, endDate)
            .ToListAsync(cancellationToken);

        var periods = PromotionPauseQueries.PeriodsQuery(dbContext, academicYearId, levelId, startDate, endDate);

        int periodsSpanning = await periods.CountAsync(cancellationToken);
        int periodsPlanned = await periods.Where(ServicePeriodLifecycle.Planned).CountAsync(cancellationToken);
        int periodsUnderway = await periods.Where(ServicePeriodLifecycle.Underway).CountAsync(cancellationToken);

        int studentsAffected = await periods
            .Select(p => p.InternshipAssignment.RegistrationId)
            .Distinct()
            .CountAsync(cancellationToken);

        var cellsBySlot = cells
            .GroupBy(c => c.SlotId)
            .ToDictionary(g => g.Key, g => g.Count());

        var slotImpacts = slots
            .Select(s => new PromotionPauseSlotImpact(
                s.SlotId, s.StageId, s.StageName, s.PeriodNumber, s.Label, s.StartDate, s.EndDate,
                before.Count(s.StartDate, s.EndDate),
                after.Count(s.StartDate, s.EndDate),
                cellsBySlot.GetValueOrDefault(s.SlotId)))
            .ToList();

        var stageImpacts = slotImpacts
            .GroupBy(s => (s.StageId, s.StageName))
            .Select(g => new PromotionPauseStageImpact(
                g.Key.StageId,
                g.Key.StageName,
                slots.First(s => s.StageId == g.Key.StageId).StatedDurationInDays,
                g.Count(),
                g.Sum(s => s.WorkingDaysBefore - s.WorkingDaysAfter),
                g.Min(s => s.WorkingDaysAfter),
                g.Max(s => s.WorkingDaysAfter)))
            .OrderBy(s => s.Name)
            .ToList();

        // ⚠ Whether the axis can be re-laid at all. Measured 2026-09-06 on the live base: the 3ᵉ MED
        // holds 804 published cells, so « reposez l'axe » — the remedy this report used to name — is a
        // button that refuses. A report that prescribes a refused act is worse than one that prescribes
        // nothing.
        int publishedCells = await PromotionPauseQueries
            .PublishedCellsQuery(dbContext, academicYearId, levelId)
            .CountAsync(cancellationToken);

        int workingDaysLost = before.Count(startDate, endDate);

        // ⚠ Asked of the whole academic YEAR, never of the window — and that is the correction of a
        // caption that was noise. An exam week holds no jour férié in the ordinary case, so measured on
        // its own span the flag fired on nearly every window and said nothing; measured on the year, it
        // says the one thing worth acting on: nobody has entered this year's calendar, so « jours
        // ouvrables » here means "hors week-end". Same reasoning as MissingReligious being widened to
        // whole Gregorian years.
        //
        // ⚠ Faculty closures only. A promotion pause recorded in the year would otherwise make the
        // calendar read "not empty" on the strength of a window rather than of a holiday, which is the
        // opposite of what the caption tells the reader to go and check.
        bool calendarIsEmpty = !before
            .HolidaysBetween(year.StartDate, year.EndDate)
            .Any(c => c.Scope == CalendarClosureScope.Faculty);

        return new PromotionPauseImpactResponse(
            academicYearId,
            year.Label,
            levelId,
            levelLabel,
            startDate,
            endDate,
            endDate.DayNumber - startDate.DayNumber + 1,
            workingDaysLost,
            calendarIsEmpty,
            slots.Count,
            cells.Count,
            cells.Select(c => c.CohortId).Distinct().Count(),
            periodsSpanning,
            periodsPlanned,
            periodsUnderway,
            periodsSpanning - periodsPlanned - periodsUnderway,
            studentsAffected,
            stageImpacts,
            slotImpacts.Take(MaxSlotRows).ToList(),
            slotImpacts.Count > MaxSlotRows,
            publishedCells,
            Warnings(workingDaysLost, calendarIsEmpty, slots.Count, periodsSpanning, periodsUnderway,
                publishedCells));
    }

    /// <summary>
    /// Said only when the data says it. ⚠ A caption that fires whatever was found is noise, and noise
    /// is dismissed — which puts the one that matters out of sight.
    /// </summary>
    private static List<string> Warnings(
        int workingDaysLost, bool calendarIsEmpty, int slots, int periods, int periodsUnderway,
        int publishedCells)
    {
        var warnings = new List<string>();

        if (workingDaysLost == 0)
            warnings.Add(
                "Cette fenêtre ne coûte aucun jour ouvrable : elle ne couvre que des week-ends ou des "
                + "jours déjà fériés.");

        if (calendarIsEmpty)
            warnings.Add(
                "Aucun jour férié n'est enregistré pour cette année universitaire, donc « jours "
                + "ouvrables » veut dire ici « hors week-end ». Complétez le calendrier de la faculté "
                + "pour un décompte juste.");

        // ⚠ « Rien n'est encore planifié » and « rien n'est touché » are opposite situations and the
        // same zero. The promotion having no grid at all is the normal state before a répartition, and
        // it is the case where declaring the window in advance is exactly right.
        if (slots == 0 && periods == 0)
            warnings.Add(
                "Aucun créneau ni aucune rotation ne traverse cette fenêtre : rien n'est à reposer, et "
                + "l'axe qui sera posé ensuite l'enjambera de lui-même.");
        else if (publishedCells > 0)
            // ⚠ The case measured on the live base, and the one the report used to get wrong: with a
            // published axis, re-laying is REFUSED (ApplyRotationCycleCommand, PublishedCells > 0). The
            // window is recorded and correct — the promotion simply loses those days from the columns
            // it already holds, and moving a published column is PHASES.md §17.1, which is not built.
            warnings.Add(
                $"⚠ L'axe de cette promotion est déjà publié ({publishedCells} cellule(s)), donc le "
                + $"reposer est refusé. Les {slots} créneau(x) traversés gardent leurs dates et la "
                + $"promotion perd ces jours"
                + (periodsUnderway > 0 ? $", dont {periodsUnderway} rotation(s) en cours" : string.Empty)
                + ". La fenêtre reste juste et sera prise en compte par tout axe posé ensuite ; déplacer "
                + "une colonne déjà publiée n'est pas encore possible.");
        else if (periodsUnderway > 0)
            warnings.Add(
                $"{periodsUnderway} rotation(s) sont en cours pendant cette fenêtre. Elles gardent leurs "
                + "dates : la fenêtre retire des jours du stage plutôt que de le prolonger. Reposer l'axe "
                + "de la promotion est le geste qui les rattrape.");
        else if (slots > 0)
            warnings.Add(
                $"{slots} créneau(x) traversent cette fenêtre et gardent leurs dates. Reposez l'axe de la "
                + "promotion pour que les colonnes l'enjambent.");

        return warnings;
    }
}
