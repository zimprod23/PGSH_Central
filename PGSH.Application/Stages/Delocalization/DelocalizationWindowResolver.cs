using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delocalization;

/// <summary>
/// Works out, for each cohorte named, the window a délocalisation on this stage is recorded under
/// when the caller supplies none.
/// </summary>
/// <remarks>
/// <para>⚠ <b>The window belongs to the cohorte, not to the stage.</b> Until 2026-09-10 it was
/// <c>min(StartDate)</c>/<c>max(EndDate)</c> over <em>every</em> créneau of the stage — the whole
/// axis. On a stage the promotion crosses in six partitions that is six times the passage any one
/// group actually makes: a 4ᵉ MED délocalisé on Cardiologie carried 16/11/2026 → 25/03/2027 into his
/// dossier for a stage his group serves in one période. Every other stage of his year then overlapped
/// it, which is a contradiction the dossier, the parcours and the export all read as fact.</para>
///
/// <para>⚠ <b>It refuses rather than inventing a window.</b> A stage whose grid has never been
/// authored has none at all — the imported years carry 105 626 periods behind zero créneaux — and a
/// fabricated pair of dates would sit in the dossier looking exactly like a recorded fact. Asking for
/// them is a sentence the operator can act on.</para>
///
/// <para><b>A cohorte with no cell falls back to the axis, and says so.</b> The répartition may not be
/// arranged yet, and délocalising a stage nobody has planned is a case the act deliberately supports —
/// so refusing there would remove a working path to close a defect on another. What it must not do is
/// hide which of the two happened, hence <see cref="DelocalizationWindowSource"/>.</para>
///
/// <para>The lookup it returns is <b>total over the cohortes it was asked about</b>: every one of them
/// has a window or the whole call failed, so the caller never faces an absent entry.</para>
/// </remarks>
internal static class DelocalizationWindowResolver
{
    /// <summary>
    /// The dates the given cohortes occupy on this stage, one entry per cohorte asked about.
    /// </summary>
    public static async Task<Result<IReadOnlyDictionary<int, DelocalizationWindow>>> ResolveAsync(
        IApplicationDbContext dbContext,
        int stageId,
        int academicYearId,
        IReadOnlyCollection<int> cohortIds,
        string stageName,
        string yearLabel,
        CancellationToken ct)
    {
        if (cohortIds.Count == 0)
            return Result.Success<IReadOnlyDictionary<int, DelocalizationWindow>>(
                new Dictionary<int, DelocalizationWindow>());

        var byCohort = await ResolveCohortSpansAsync(dbContext, cohortIds, ct);

        // Only fetched when somebody actually needs it — a répartition that is arranged answers every
        // cohorte from its own cells and never reads the axis at all.
        var axis = byCohort.Count == cohortIds.Count
            ? null
            : await ResolveAxisAsync(dbContext, stageId, academicYearId, ct);

        var windows = new Dictionary<int, DelocalizationWindow>(cohortIds.Count);

        foreach (int cohortId in cohortIds)
        {
            if (byCohort.TryGetValue(cohortId, out var cohortWindow))
            {
                windows[cohortId] = cohortWindow;
                continue;
            }

            if (axis is not { } fallback)
                return Result.Failure<IReadOnlyDictionary<int, DelocalizationWindow>>(
                    StageErrors.NoWindowForDelocalization(stageName, yearLabel));

            windows[cohortId] = fallback;
        }

        return windows;
    }

    /// <summary>The same question for a single cohorte — the one-student act asks it this way.</summary>
    public static async Task<Result<DelocalizationWindow>> ResolveOneAsync(
        IApplicationDbContext dbContext,
        int stageId,
        int academicYearId,
        int cohortId,
        string stageName,
        string yearLabel,
        CancellationToken ct)
    {
        var windows = await ResolveAsync(
            dbContext, stageId, academicYearId, [cohortId], stageName, yearLabel, ct);

        return windows.IsFailure
            ? Result.Failure<DelocalizationWindow>(windows.Error)
            : windows.Value[cohortId];
    }

    /// <summary>
    /// Every cell these cohortes hold, flat, with the dates of the créneau behind it.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Flat and top-level, folded in memory.</b> Grouping by cohorte and aggregating
    /// <c>Min</c>/<c>Max</c> in SQL is the shape Npgsql is entitled to refuse — it is what killed the
    /// macro plan on its first real request with the whole suite green. Named so
    /// <c>SqlTranslationTests</c> can compile it without a database.
    /// </remarks>
    internal static IQueryable<CohortSpan> CohortSpansQuery(
        IApplicationDbContext dbContext, IReadOnlyList<int> cohortIds) =>
        dbContext.CohortSlotAssignments
            .Where(c => cohortIds.Contains(c.CohortId))
            .Select(c => new CohortSpan(c.CohortId, c.StageSlot.StartDate, c.StageSlot.EndDate));

    /// <summary>The stage's whole axis for one promotion — the fallback, and only the fallback.</summary>
    internal static IQueryable<Span> AxisQuery(
        IApplicationDbContext dbContext, int stageId, int academicYearId) =>
        dbContext.StageSlots
            .Where(s => s.StageId == stageId && s.AcademicYearId == academicYearId)
            .Select(s => new Span(s.StartDate, s.EndDate));

    private static async Task<Dictionary<int, DelocalizationWindow>> ResolveCohortSpansAsync(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> cohortIds, CancellationToken ct)
    {
        var spans = await CohortSpansQuery(dbContext, cohortIds.ToList())
            .AsNoTracking()
            .ToListAsync(ct);

        return spans
            .GroupBy(s => s.CohortId)
            .ToDictionary(
                g => g.Key,
                g => new DelocalizationWindow(
                    g.Min(s => s.Start), g.Max(s => s.End), DelocalizationWindowSource.Cohort));
    }

    private static async Task<DelocalizationWindow?> ResolveAxisAsync(
        IApplicationDbContext dbContext, int stageId, int academicYearId, CancellationToken ct)
    {
        var slots = await AxisQuery(dbContext, stageId, academicYearId).AsNoTracking().ToListAsync(ct);

        return slots.Count == 0
            ? null
            : new DelocalizationWindow(
                slots.Min(s => s.Start), slots.Max(s => s.End), DelocalizationWindowSource.StageAxis);
    }

    internal sealed record CohortSpan(int CohortId, DateOnly Start, DateOnly End);

    internal sealed record Span(DateOnly Start, DateOnly End);
}
