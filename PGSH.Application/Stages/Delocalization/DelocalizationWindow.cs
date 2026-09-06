using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delocalization;

/// <summary>
/// The dates a délocalisation is recorded under when the caller names none: the stage's own window
/// for that promotion, taken from its créneaux.
/// </summary>
/// <remarks>
/// <para>The dates are the faculty's, not the external hospital's. What a student actually does once
/// he is in Kénitra follows that hospital's own calendar, and the app has no way to learn it; what
/// PGSH records is the period the stage officially occupies, which is what every other read — the
/// dossier, the parcours, the export — is measured against. The caller may still override them per
/// student, because sometimes scolarité does know.</para>
///
/// <para>⚠ <b>It refuses rather than inventing a window.</b> A stage whose grid has never been
/// authored has none at all — the imported years carry 105 626 periods behind zero créneaux — and a
/// fabricated pair of dates would sit in the dossier looking exactly like a recorded fact. Asking for
/// them is a sentence the operator can act on.</para>
/// </remarks>
internal static class DelocalizationWindow
{
    public static async Task<Result<(DateOnly Start, DateOnly End)>> ResolveAsync(
        IApplicationDbContext dbContext,
        int stageId,
        int academicYearId,
        string stageName,
        string yearLabel,
        CancellationToken ct)
    {
        // Materialised rather than aggregated in SQL: a stage holds a handful of créneaux, and Min
        // over a grouping-by-constant is the kind of shape a provider is entitled to refuse.
        var slots = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.StageId == stageId && s.AcademicYearId == academicYearId)
            .Select(s => new { s.StartDate, s.EndDate })
            .ToListAsync(ct);

        return slots.Count == 0
            ? Result.Failure<(DateOnly, DateOnly)>(
                StageErrors.NoWindowForDelocalization(stageName, yearLabel))
            : Result.Success((slots.Min(s => s.StartDate), slots.Max(s => s.EndDate)));
    }
}
