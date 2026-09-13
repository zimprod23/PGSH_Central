using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;

/// <summary>One affectation the undo acts on.</summary>
internal sealed record ReversalWorkItem(
    Guid EntryId,
    Guid InternshipAssignmentId,
    int StageId,
    AffectationImportOutcome Outcome,
    IReadOnlyList<RestoredPeriod> Restore);

internal sealed record AffectationImportReversalPlan(
    Guid ImportId,
    IReadOnlyList<ReversalWorkItem> Work,
    AffectationImportReversalReport Report);

/// <summary>
/// Turns a recorded import into the undo the act executes — and into the report the operator is shown.
/// One class, run by both, for the same reason <see cref="AffectationSheetPlanner"/> is: an aperçu
/// computed one way and a write performed another are two rules with nothing able to catch them
/// disagreeing, and here the second one deletes affectations.
/// </summary>
/// <remarks>
/// <para>⚠ <b>An undo is only honest while the thing it undoes is still what it wrote.</b> Every
/// refusal here says the same sentence in a different way: something happened <i>since</i> — a mark, a
/// day of attendance, a re-planning — so walking the import back would not restore a previous state,
/// it would impose an old one over somebody's work. The import's own guards make this checkable:
/// because it refused to destroy marks and attendance on the way in, anything it finds now is new.</para>
///
/// <para>⚠ <b>Reads are flat and top-level</b>, folded in memory, like everywhere else — a collection
/// inside a projection is the shape Npgsql refuses.</para>
/// </remarks>
internal sealed class AffectationImportReversalPlanner(IApplicationDbContext dbContext)
{
    private const int MaximumReportedRows = 500;

    public async Task<Result<AffectationImportReversalPlan>> PlanAsync(Guid importId, CancellationToken ct)
    {
        var import = await ImportQuery(dbContext, importId).AsNoTracking().FirstOrDefaultAsync(ct);
        if (import is null)
            return Result.Failure<AffectationImportReversalPlan>(
                StageErrors.AffectationImportNotFound(importId));

        if (import.Status == AffectationImportStatus.Reversed)
            return Result.Failure<AffectationImportReversalPlan>(
                StageErrors.AffectationImportAlreadyReversed(importId));

        var entries = await EntriesQuery(dbContext, importId).AsNoTracking().ToListAsync(ct);
        var replaced = await ReplacedPeriodsQuery(dbContext, importId).AsNoTracking().ToListAsync(ct);

        var assignmentIds = entries.Select(e => e.InternshipAssignmentId).ToList();
        // Existence only — the reversal needs to know the affectation is still there, nothing else
        // about it. A projection to the key tracks nothing, so no AsNoTracking to state.
        var live = await LiveAffectationIdsQuery(dbContext, assignmentIds).ToListAsync(ct);
        var livePeriods = await LivePeriodsQuery(dbContext, assignmentIds).AsNoTracking().ToListAsync(ct);

        var liveById = live.ToHashSet();
        var periodsById = livePeriods
            .GroupBy(p => p.AssignmentId)
            .ToDictionary(g => g.Key, IReadOnlyList<LivePeriod> (g) => g.ToList());
        var replacedByEntry = replaced
            .GroupBy(r => r.EntryId)
            .ToDictionary(g => g.Key, IReadOnlyList<ReplacedRow> (g) => g.ToList());

        var work = new List<ReversalWorkItem>();
        var rows = new List<AffectationImportReversalRow>();

        foreach (var entry in entries)
        {
            var restore = replacedByEntry.TryGetValue(entry.EntryId, out var found) ? found : [];

            if (!liveById.Contains(entry.InternshipAssignmentId))
            {
                rows.Add(Row(entry, AffectationImportReversalRowStatus.AlreadyGone, 0));
                continue;
            }

            var held = periodsById.TryGetValue(entry.InternshipAssignmentId, out var p) ? p : [];

            if (held.Any(x => x.HasEvaluation))
            {
                rows.Add(Row(entry, AffectationImportReversalRowStatus.EvaluatedSince, restore.Count));
                continue;
            }

            if (held.Any(x => x.HasAttendance))
            {
                rows.Add(Row(entry, AffectationImportReversalRowStatus.AttendedSince, restore.Count));
                continue;
            }

            // ⚠ The affectation must still hold exactly the périodes the import left on it. Anything
            // else — a re-planning, a transfer, a second import — means the undo would be writing an
            // old state over a newer one that somebody chose.
            if (!StillWhatTheImportLeft(entry, held))
            {
                rows.Add(Row(entry, AffectationImportReversalRowStatus.ChangedSince, restore.Count));
                continue;
            }

            var status = entry.Outcome == AffectationImportOutcome.Created
                ? AffectationImportReversalRowStatus.WillRemoveAffectation
                : AffectationImportReversalRowStatus.WillRestorePeriods;

            rows.Add(Row(entry, status, restore.Count));
            work.Add(new ReversalWorkItem(
                entry.EntryId,
                entry.InternshipAssignmentId,
                entry.StageId,
                entry.Outcome,
                [.. restore.Select(Restored)]));
        }

        return new AffectationImportReversalPlan(importId, work, BuildReport(import, rows, work, replaced));
    }

    /// <summary>
    /// Does this affectation still hold what the import left on it?
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Asked as « no période came from the grid, and the count is what we wrote »</b> rather than
    /// by comparing services and dates. The import wrote ad-hoc périodes — never a grid cell — so a
    /// période linked to one can only have arrived since, through a publication. It is a cheap, total
    /// check that cannot be fooled by a correction that happens to keep the same shape.
    /// </remarks>
    private static bool StillWhatTheImportLeft(EntryRow entry, IReadOnlyList<LivePeriod> held) =>
        held.All(p => p.CohortSlotAssignmentId is null)
        && held.Count == entry.WrittenPeriods;

    private static RestoredPeriod Restored(ReplacedRow r) => new(
        r.ServiceId, r.StartDate, r.EndDate,
        r.IsStarted, r.IsComplete, r.IsInterrupted, r.IsPaused, r.IsDelocalized,
        r.CohortSlotAssignmentId, r.DelocalizationReason);

    private static AffectationImportReversalRow Row(
        EntryRow entry, AffectationImportReversalRowStatus status, int toRestore) =>
        new(entry.RegistrationId,
            $"{entry.FirstName} {entry.LastName}".Trim(),
            entry.Appogee,
            entry.StageName,
            status,
            status == AffectationImportReversalRowStatus.WillRestorePeriods ? toRestore : 0,
            Describe(status));

    private static string Describe(AffectationImportReversalRowStatus status) => status switch
    {
        AffectationImportReversalRowStatus.WillRemoveAffectation =>
            "L'import avait créé cette affectation : elle sera supprimée, avec ses périodes.",
        AffectationImportReversalRowStatus.WillRestorePeriods =>
            "Les périodes d'avant l'import seront réécrites telles qu'elles étaient.",
        AffectationImportReversalRowStatus.AlreadyGone =>
            "L'affectation n'existe plus : rien à défaire sur cette ligne.",
        AffectationImportReversalRowStatus.EvaluatedSince =>
            "Ce stage a été évalué depuis l'import : l'annuler supprimerait la note.",
        AffectationImportReversalRowStatus.AttendedSince =>
            "Des journées de présence ont été saisies depuis l'import : l'annuler les supprimerait.",
        AffectationImportReversalRowStatus.ChangedSince =>
            "Ce stage a été replanifié ou publié depuis l'import : l'annuler écraserait ce qui a été fait depuis.",
        _ => string.Empty,
    };

    private static AffectationImportReversalReport BuildReport(
        ImportRow import,
        List<AffectationImportReversalRow> rows,
        List<ReversalWorkItem> work,
        List<ReplacedRow> _)
    {
        int errors = rows.Count(r => r.Status.IsError());
        var restoring = work.Where(w => w.Outcome != AffectationImportOutcome.Created).ToList();

        var restoredIds = restoring.SelectMany(w => w.Restore).ToList();

        var reported = rows
            .OrderByDescending(r => r.Status.NeedsAttention())
            .ThenBy(r => r.StudentFullName, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaximumReportedRows)
            .ToList();

        var notes = new List<string>();

        if (errors > 0)
            notes.Add($"{errors} affectation(s) ont changé depuis l'import : rien ne sera défait tant "
                    + "qu'elles ne sont pas traitées à la main. Elles sont listées en tête.");

        int toRemove = work.Count(w => w.Outcome == AffectationImportOutcome.Created);
        if (toRemove > 0)
            notes.Add($"{toRemove} affectation(s) seront supprimées : l'import les avait créées, il n'y "
                    + "avait rien avant elles. Rien ne les remet, sauf un nouveau fichier.");

        int published = restoredIds.Count(p => p.CohortSlotAssignmentId is not null);
        if (published > 0)
            notes.Add($"{published} période(s) retrouveront leur cellule de la grille : le planning et "
                    + "les enregistrements d'exécution s'accorderont de nouveau.");

        if (rows.Count(r => r.Status == AffectationImportReversalRowStatus.AlreadyGone) > 0)
            notes.Add("Certaines affectations n'existent plus ; ces lignes sont comptées et ignorées.");

        return new AffectationImportReversalReport(
            import.Id,
            import.YearLabel,
            import.LevelLabel ?? $"Niveau {import.LevelId}",
            import.AppliedAtUtc,
            rows.Count,
            toRemove,
            restoring.Count,
            restoredIds.Count,
            published,
            work.Count(w => w.Outcome == AffectationImportOutcome.Created),
            rows.Count(r => r.Status == AffectationImportReversalRowStatus.AlreadyGone),
            errors,
            CanApply: errors == 0,
            reported,
            RowsTruncated: rows.Count > MaximumReportedRows,
            notes);
    }

    // ─── The queries, named so they can be compiled without a database ────────

    internal static IQueryable<ImportRow> ImportQuery(IApplicationDbContext dbContext, Guid importId) =>
        dbContext.AffectationImports
            .Where(i => i.Id == importId)
            .Select(i => new ImportRow(
                i.Id, i.AcademicYearId, i.AcademicYear.Label, i.LevelId,
                dbContext.Levels.Where(l => l.Id == i.LevelId).Select(l => l.Label).FirstOrDefault(),
                i.AppliedAtUtc, i.Status));

    internal static IQueryable<EntryRow> EntriesQuery(IApplicationDbContext dbContext, Guid importId) =>
        dbContext.AffectationImportEntries
            .Where(e => e.AffectationImportId == importId)
            .Select(e => new EntryRow(
                e.Id,
                e.RegistrationId,
                e.StageId,
                dbContext.Stages.Where(s => s.Id == e.StageId).Select(s => s.Name).FirstOrDefault() ?? "",
                e.InternshipAssignmentId,
                e.Outcome,
                dbContext.Registrations.Where(r => r.Id == e.RegistrationId)
                    .Select(r => r.Student.FirstName).FirstOrDefault() ?? "",
                dbContext.Registrations.Where(r => r.Id == e.RegistrationId)
                    .Select(r => r.Student.LastName).FirstOrDefault() ?? "",
                dbContext.Registrations.Where(r => r.Id == e.RegistrationId)
                    .Select(r => r.Student.Appogee).FirstOrDefault(),
                e.WrittenPeriodCount));

    internal static IQueryable<ReplacedRow> ReplacedPeriodsQuery(
        IApplicationDbContext dbContext, Guid importId) =>
        dbContext.AffectationImportEntries
            .Where(e => e.AffectationImportId == importId)
            .SelectMany(e => e.ReplacedPeriods)
            .Select(p => new ReplacedRow(
                p.AffectationImportEntryId,
                p.ServiceId, p.StartDate, p.EndDate,
                p.IsStarted, p.IsComplete, p.IsInterrupted, p.IsPaused, p.IsDelocalized,
                p.CohortSlotAssignmentId, p.DelocalizationReason));

    internal static IQueryable<Guid> LiveAffectationIdsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<Guid> assignmentIds) =>
        dbContext.InternshipAssignments
            .Where(a => assignmentIds.Contains(a.Id))
            .Select(a => a.Id);

    /// <summary>
    /// ⚠ Flat, keyed on the affectation. <c>HasAttendance</c> is <c>Any()</c> inside the projection —
    /// a correlated <c>EXISTS</c>, which translates — never a folded collection, which does not.
    /// </summary>
    internal static IQueryable<LivePeriod> LivePeriodsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<Guid> assignmentIds) =>
        dbContext.ServicePeriods
            .Where(p => assignmentIds.Contains(p.InternshipAssignmentId))
            .Select(p => new LivePeriod(
                p.InternshipAssignmentId,
                p.CohortSlotAssignmentId,
                p.Evaluation != null,
                p.Attendance.Any()));
}

internal sealed record ImportRow(
    Guid Id, int AcademicYearId, string YearLabel, int LevelId, string? LevelLabel,
    DateTime AppliedAtUtc, AffectationImportStatus Status);

internal sealed record EntryRow(
    Guid EntryId, Guid RegistrationId, int StageId, string StageName, Guid InternshipAssignmentId,
    AffectationImportOutcome Outcome, string FirstName, string LastName, string? Appogee,
    int WrittenPeriods);

internal sealed record ReplacedRow(
    Guid EntryId, int ServiceId, DateOnly StartDate, DateOnly EndDate,
    bool IsStarted, bool IsComplete, bool IsInterrupted, bool IsPaused, bool IsDelocalized,
    int? CohortSlotAssignmentId, string? DelocalizationReason);

internal sealed record LivePeriod(
    Guid AssignmentId, int? CohortSlotAssignmentId, bool HasEvaluation, bool HasAttendance);
