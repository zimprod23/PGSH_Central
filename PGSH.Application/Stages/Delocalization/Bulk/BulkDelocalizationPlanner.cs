using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;
using PGSH.Application.Students.Selection;

namespace PGSH.Application.Stages.Delocalization.Bulk;

/// <summary>
/// Works out what délocalising a selection of students would do, and hands the apply the aggregates
/// to do it with. The preview and the apply both run this and nothing else, so what the operator was
/// shown is what executes.
/// </summary>
/// <remarks>
/// <para><b>It answers one question: what would happen to these students.</b> <i>Which</i> students the
/// operator meant is <see cref="DelocalizationTargetResolver"/>'s, and the split is not cosmetic —
/// the two fail for unrelated reasons, and a class owning both an identifier grammar and a
/// délocalisation's preconditions had no single reason to change.</para>
///
/// <para>⚠ <b>Scoped to one academic year throughout.</b> Every id is checked against that year on
/// both sides of the split: a roster is keyed (year, level, number), so a group id belongs to exactly
/// one promotion, and a registration named from a pasted CNE may well be last year's.</para>
///
/// <para>⚠ <b>Nothing here is <c>AsNoTracking</c> by accident.</b> The projections that only feed the
/// report are; the assignments are not, because they are the objects the apply mutates. Marking a
/// shared query no-tracking is how a bulk apply came to report success having written nothing.</para>
/// </remarks>
internal sealed class BulkDelocalizationPlanner(
    IApplicationDbContext dbContext,
    StudentSelectionResolver targetResolver,
    AcademicYearResolver yearResolver)
{
    public async Task<Result<BulkDelocalizationPlan>> PlanAsync(
        int stageId,
        int serviceId,
        int? academicYearId,
        StudentTargets targets,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken ct)
    {
        var year = await yearResolver.ResolveWithLabelAsync(academicYearId, ct);
        if (year.IsFailure)
            return Result.Failure<BulkDelocalizationPlan>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        var stage = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == stageId)
            .Select(s => new { s.Id, s.Name })
            .FirstOrDefaultAsync(ct);

        if (stage is null)
            return Result.Failure<BulkDelocalizationPlan>(StageErrors.NotFound(stageId));

        var service = await dbContext.Services
            .AsNoTracking()
            .Where(s => s.Id == serviceId)
            .Select(s => new { s.Id, s.Name, s.IsExternal })
            .FirstOrDefaultAsync(ct);

        if (service is null)
            return Result.Failure<BulkDelocalizationPlan>(ServiceErrors.NotFound(serviceId));

        // Who the operator named, and a row for every line that named nobody. A separate question
        // from the one below — and one that fails for entirely unrelated reasons.
        var selection = await targetResolver.ResolveAsync(targets, yearId, ct);
        var candidates = selection.RegistrationIds;

        // The resolver reports in its own vocabulary — « introuvable » / « autre année » — and each
        // act maps it onto its own row states. Mapped rather than shared outright because the two
        // enums answer different questions: one is about finding a student, the other about what the
        // act would do to him.
        var rows = selection.Unresolved.Select(Refuse).ToList();

        var registrations = await LoadRegistrationsAsync(candidates.Keys.ToList(), ct);
        var cohorts = await LoadCohortsAsync(stageId, registrations, ct);
        var assignments = await LoadAssignmentsAsync(stageId, registrations.Select(r => r.Id), ct);

        // ⚠ Resolved here, and per cohorte — after the students are known rather than before. A
        // single window read off the stage before anybody was named is what dated a whole selection
        // by the entire axis, whatever partition each student was actually in.
        var windows = await ResolveWindowsAsync(
            stageId, yearId, stage.Name, yearLabel, cohorts.Values, startDate, endDate, ct);

        if (windows.IsFailure)
            return Result.Failure<BulkDelocalizationPlan>(windows.Error);

        var work = new List<PlannedDelocalization>();

        foreach (var registration in registrations
                     .OrderBy(r => r.GroupLabel)
                     .ThenBy(r => r.StudentName))
        {
            string? source = candidates[registration.Id];

            if (registration.AcademicGroupId is not { } groupId)
            {
                rows.Add(Refuse(registration, BulkDelocalizationRowStatus.NoRoster,
                    $"Aucun groupe pour {yearLabel} : affectez l'étudiant à un groupe avant de délocaliser.",
                    source));
                continue;
            }

            if (!cohorts.TryGetValue(groupId, out int cohortId))
            {
                rows.Add(Refuse(registration, BulkDelocalizationRowStatus.NoCohort,
                    $"Le groupe « {registration.GroupLabel ?? groupId.ToString()} » n'a pas de cohorte "
                    + $"sur « {stage.Name} » — vérifiez qu'il s'agit bien de la promotion concernée.",
                    source));
                continue;
            }

            assignments.TryGetValue(registration.Id, out var assignment);

            // No assignment at all is not a refusal: the stage was never planned for this student and
            // the délocalisation creates the record, exactly as the single-student act does.
            var preflight = assignment?.PreflightDelocalization();

            if (preflight is { CanDelocalize: false })
            {
                rows.Add(Refuse(registration, BulkDelocalizationRowStatus.AlreadyMarked,
                    $"Une évaluation est enregistrée sur ce stage ({preflight.MarkedPeriods} période(s)) : "
                    + "la délocalisation l'effacerait.",
                    source));
                continue;
            }

            var (status, message) = preflight switch
            {
                { AlreadyDelocalized: true } => (
                    BulkDelocalizationRowStatus.WillReplace,
                    "Déjà délocalisé : la période sera remplacée par les dates et le motif de cette opération."),

                { UnderwayPeriods: > 0 } => (
                    BulkDelocalizationRowStatus.WillDropUnderway,
                    $"{preflight.UnderwayPeriods} rotation(s) commencée(s) seront supprimées — l'étudiant "
                    + "part avant la fin du stage."),

                _ => (
                    BulkDelocalizationRowStatus.WillDelocalize,
                    preflight is null
                        ? "Aucune rotation planifiée : la délocalisation crée le stage."
                        : $"{preflight.DroppedPeriods} rotation(s) planifiée(s) seront remplacées."),
            };

            var window = windows.Value[cohortId];

            rows.Add(new BulkDelocalizationRow(
                registration.Id, registration.StudentName, registration.Cne, registration.Appogee,
                registration.GroupLabel, status, message, source,
                window.Start, window.End, window.Source));

            work.Add(new PlannedDelocalization(registration.Id, cohortId, assignment, window));
        }

        var report = BulkDelocalizationReport.From(
            stage.Id, stage.Name, service.Id, service.Name, service.IsExternal,
            yearId, yearLabel, rows);

        return new BulkDelocalizationPlan(report, work, stageId, serviceId);
    }

    /// <summary>
    /// The window each cohorte of the selection is délocalisé under: the operator's dates when he
    /// supplied them, and otherwise the cohorte's own passage through the stage.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Dates the operator names govern every row.</b> He is stating what the external hospital
    /// did, which is one fact about one act — the per-cohorte answer is what PGSH <em>derives</em>
    /// when he does not, and deriving over an explicit statement would be the app overruling him.
    /// </remarks>
    private async Task<Result<IReadOnlyDictionary<int, DelocalizationWindow>>> ResolveWindowsAsync(
        int stageId, int yearId, string stageName, string yearLabel,
        IEnumerable<int> cohortIds, DateOnly? startDate, DateOnly? endDate, CancellationToken ct)
    {
        var ids = cohortIds.Distinct().ToList();

        if (startDate is { } start && endDate is { } end)
            return Result.Success<IReadOnlyDictionary<int, DelocalizationWindow>>(
                ids.ToDictionary(
                    id => id,
                    _ => new DelocalizationWindow(start, end, DelocalizationWindowSource.Named)));

        return await DelocalizationWindowResolver.ResolveAsync(
            dbContext, stageId, yearId, ids, stageName, yearLabel, ct);
    }

    /// <summary>
    /// The assignments this stage holds for these students — the aggregates the apply mutates.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Tracked, and the evaluations Included.</b>
    /// <see cref="InternshipAssignment.PreflightDelocalization"/> reads them, and an un-Included
    /// evaluation is indistinguishable from an absent one — which would report « rien à perdre » on
    /// a stage carrying a mark. Named so <c>SqlTranslationTests</c> can compile it.
    /// </remarks>
    internal static IQueryable<InternshipAssignment> AssignmentsQuery(
        IApplicationDbContext dbContext, int stageId, IReadOnlyList<Guid> registrationIds) =>
        dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.Evaluation)
            .Where(a => registrationIds.Contains(a.RegistrationId) && a.Cohort.StageId == stageId);

    private async Task<List<RegistrationInfo>> LoadRegistrationsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        var idList = ids.ToList();

        return await dbContext.Registrations
            .AsNoTracking()
            .Where(r => idList.Contains(r.Id))
            .Select(r => new RegistrationInfo(
                r.Id,
                r.AcademicGroupId,
                ((r.Student.FirstName ?? "") + " " + (r.Student.LastName ?? "")).Trim(),
                r.Student.CNE,
                r.Student.Appogee,
                r.AcademicGroup != null ? r.AcademicGroup.Label : null))
            .ToListAsync(ct);
    }

    private async Task<Dictionary<int, int>> LoadCohortsAsync(
        int stageId, IReadOnlyCollection<RegistrationInfo> registrations, CancellationToken ct)
    {
        var groupIds = registrations
            .Where(r => r.AcademicGroupId is not null)
            .Select(r => r.AcademicGroupId!.Value)
            .Distinct()
            .ToList();

        if (groupIds.Count == 0)
            return [];

        return await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == stageId && groupIds.Contains(c.AcademicGroupId))
            .ToDictionaryAsync(c => c.AcademicGroupId, c => c.Id, ct);
    }

    private async Task<Dictionary<Guid, InternshipAssignment>> LoadAssignmentsAsync(
        int stageId, IEnumerable<Guid> registrationIds, CancellationToken ct)
    {
        var idList = registrationIds.ToList();
        if (idList.Count == 0)
            return [];

        var assignments = await AssignmentsQuery(dbContext, stageId, idList).ToListAsync(ct);

        return assignments.ToDictionary(a => a.RegistrationId);
    }

    /// <summary>
    /// One unresolved line of the selection, in this act's vocabulary.
    /// </summary>
    private static BulkDelocalizationRow Refuse(UnresolvedTarget target) =>
        new(target.RegistrationId, target.StudentName, target.Cne, target.Appogee, null,
            target.Reason == TargetResolution.NotFound
                ? BulkDelocalizationRowStatus.NotFound
                : BulkDelocalizationRowStatus.WrongYear,
            target.Message, target.SourceIdentifier);

    private static BulkDelocalizationRow Refuse(
        RegistrationInfo registration, BulkDelocalizationRowStatus status, string message, string? source) =>
        new(registration.Id, registration.StudentName, registration.Cne, registration.Appogee,
            registration.GroupLabel, status, message, source);

    private sealed record RegistrationInfo(
        Guid Id,
        int? AcademicGroupId,
        string StudentName,
        string? Cne,
        string? Appogee,
        string? GroupLabel);
}
