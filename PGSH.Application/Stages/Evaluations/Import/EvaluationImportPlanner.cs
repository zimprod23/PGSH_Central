using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.Import;

/// <summary>
/// Turns an uploaded sheet into the exact set of writes it would perform, and reports every row that
/// cannot be written and why.
///
/// Preview and apply both run this and nothing else, so the dry run the user confirmed is literally
/// the plan that executes — a preview that can drift from the apply is worse than no preview. It
/// therefore loads tracked entities in both cases; the preview simply never saves.
/// </summary>
internal sealed class EvaluationImportPlanner(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
{
    public async Task<Result<EvaluationImportPlan>> PlanAsync(
        int stageId,
        EvaluationImportScope scope,
        int? periodNumber,
        EvaluationMode mode,
        IReadOnlyList<EvaluationImportRow> rows,
        CancellationToken ct)
    {
        bool stageExists = await dbContext.Stages.AnyAsync(s => s.Id == stageId, ct);
        if (!stageExists)
            return Result.Failure<EvaluationImportPlan>(StageErrors.NotFound(stageId));

        if (scope == EvaluationImportScope.SinglePeriod)
        {
            bool periodExists = await dbContext.StageSlots
                .AnyAsync(s => s.StageId == stageId && s.PeriodNumber == periodNumber, ct);

            if (!periodExists)
                return Result.Failure<EvaluationImportPlan>(
                    StageErrors.ImportPeriodNotInStage(periodNumber ?? 0, stageId));
        }

        // Everyone doing this stage, with the graph SubmitEvaluation / AmendEvaluation need.
        var assignments = await dbContext.InternshipAssignments
            .Include(a => a.Registration).ThenInclude(r => r.Student)
            .Include(a => a.Cohort)
            .Include(a => a.MembershipHistory)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Evaluation!).ThenInclude(e => e.ObjectiveScores)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.CohortSlotAssignment!).ThenInclude(c => c.StageSlot)
            .Where(a => a.Cohort.StageId == stageId)
            .ToListAsync(ct);

        // Null = administrative, may evaluate anything. Otherwise the caller's own services.
        var allowedServices = await authorizer.EvaluableServiceIdsAsync(ct);

        var byCne = Index(assignments, a => a.Registration.Student.CNE);
        var byAppogee = Index(assignments, a => a.Registration.Student.Appogee);

        var reports = new List<EvaluationImportRowReport>(rows.Count);
        var work = new List<PlannedEvaluation>();
        var seen = new HashSet<Guid>();

        foreach (var row in rows)
        {
            var resolved = Resolve(row, byCne, byAppogee, seen, scope, periodNumber, mode, allowedServices);
            reports.Add(resolved.Report);
            work.AddRange(resolved.Work);
            if (resolved.AssignmentId is { } id) seen.Add(id);
        }

        return new EvaluationImportPlan(Summarize(reports), work);
    }

    private sealed record Resolution(
        EvaluationImportRowReport Report,
        IReadOnlyList<PlannedEvaluation> Work,
        Guid? AssignmentId);

    private static Resolution Resolve(
        EvaluationImportRow row,
        IReadOnlyDictionary<string, List<InternshipAssignment>> byCne,
        IReadOnlyDictionary<string, List<InternshipAssignment>> byAppogee,
        IReadOnlySet<Guid> seen,
        EvaluationImportScope scope,
        int? periodNumber,
        EvaluationMode mode,
        IReadOnlySet<int>? allowedServices)
    {
        string? cne = Normalize(row.Cne);
        string? appogee = Normalize(row.Appogee);

        if (cne is null && appogee is null)
            return Fail(row, null, EvaluationImportRowStatus.NoIdentifier,
                "Ni CNE ni numéro Apogée — la ligne ne désigne aucun étudiant.");

        var byCneMatch = cne is not null ? byCne.GetValueOrDefault(cne) : null;
        var byAppogeeMatch = appogee is not null ? byAppogee.GetValueOrDefault(appogee) : null;

        // Both identifiers given and pointing at different people: one of the two cells is mistyped,
        // and picking either would put a grade on the wrong student's record.
        if (byCneMatch is not null && byAppogeeMatch is not null
            && !byCneMatch.Select(a => a.Id).ToHashSet().SetEquals(byAppogeeMatch.Select(a => a.Id)))
            return Fail(row, null, EvaluationImportRowStatus.InvalidValue,
                "Le CNE et le numéro Apogée de cette ligne désignent deux étudiants différents.");

        var matches = byCneMatch ?? byAppogeeMatch;
        if (matches is null)
            return Fail(row, null, EvaluationImportRowStatus.UnknownStudent,
                "Aucun étudiant de ce stage ne porte cet identifiant.");

        // Several assignments for one student on one stage means a retake (see the revalidation
        // rule): the import has no way to know which attempt a sheet row means, so it refuses
        // rather than guessing at a grade.
        if (matches.Count > 1)
            return Fail(row, StudentName(matches[0]), EvaluationImportRowStatus.NotInStage,
                "Plusieurs affectations pour cet étudiant sur ce stage (rattrapage) — à saisir individuellement.");

        var assignment = matches[0];
        string name = StudentName(assignment);

        if (seen.Contains(assignment.Id))
            return Fail(row, name, EvaluationImportRowStatus.DuplicateStudent,
                "Cet étudiant apparaît déjà plus haut dans le fichier.", assignment.Id);

        if (assignment.Status is InternshipStatus.Validated or InternshipStatus.Rejected)
            return Fail(row, name, EvaluationImportRowStatus.AlreadyRatified,
                "Le stage est déjà ratifié — ses notes ne sont plus modifiables.", assignment.Id);

        var value = ReadValue(row, mode);
        if (value.Status is { } invalid)
            return Fail(row, name, invalid, value.Message!, assignment.Id);

        var periods = TargetPeriods(assignment, scope, row.PeriodNumber ?? periodNumber);
        if (periods.Count == 0)
            return Fail(row, name, EvaluationImportRowStatus.PeriodNotFound,
                scope == EvaluationImportScope.WholeStage
                    ? "Aucune rotation à évaluer pour cet étudiant."
                    : $"L'étudiant n'a pas de rotation P{row.PeriodNumber ?? periodNumber}.",
                assignment.Id);

        // A rotation still running has no mark to give: the same guard SubmitEvaluation applies.
        var open = periods.Where(p => !p.IsComplete).ToList();
        if (open.Count > 0)
            return Fail(row, name, EvaluationImportRowStatus.PeriodNotClosed,
                open.Count == periods.Count
                    ? "La période n'est pas encore clôturée."
                    : $"{open.Count} rotation(s) sur {periods.Count} ne sont pas encore clôturées.",
                assignment.Id);

        if (allowedServices is not null && periods.Any(p => !allowedServices.Contains(p.ServiceId)))
            return Fail(row, name, EvaluationImportRowStatus.NotAllowed,
                "Vous n'êtes pas chef du service d'au moins une de ces rotations.", assignment.Id);

        var work = periods
            .Select(p => new PlannedEvaluation(assignment, p, mode, value.Mark, value.Outcome, Trim(row.Remark)))
            .ToList();

        bool overwrites = periods.Any(p => p.Evaluation is not null);
        var status = overwrites
            ? EvaluationImportRowStatus.WillOverwrite
            : EvaluationImportRowStatus.WillCreate;

        string message = overwrites
            ? $"Remplace la note déjà enregistrée ({periods.Count} rotation(s))."
            : $"Nouvelle note ({periods.Count} rotation(s)).";

        return new Resolution(
            new EvaluationImportRowReport(row.SheetRow, cne, appogee, name, status, message, periods.Count),
            work,
            assignment.Id);
    }

    /// <summary>The value the chosen mode expects, or the reason the cell cannot be used.</summary>
    private sealed record CellValue(
        decimal? Mark, EvaluationOutcome? Outcome, EvaluationImportRowStatus? Status, string? Message);

    private static CellValue ReadValue(EvaluationImportRow row, EvaluationMode mode)
    {
        if (mode == EvaluationMode.Numeric)
        {
            if (row.Mark is null)
                return new CellValue(null, null, EvaluationImportRowStatus.MissingValue,
                    "Colonne Note vide alors que l'import est en mode note chiffrée.");

            return row.Mark is < 0 or > 20
                ? new CellValue(null, null, EvaluationImportRowStatus.InvalidValue,
                    $"Note « {row.Mark} » hors de l'échelle 0–20.")
                : new CellValue(row.Mark, null, null, null);
        }

        string? verdict = Normalize(row.Verdict);
        if (verdict is null)
            return new CellValue(null, null, EvaluationImportRowStatus.MissingValue,
                "Colonne Résultat vide alors que l'import est en mode validation.");

        return ParseVerdict(verdict) is { } outcome
            ? new CellValue(null, outcome, null, null)
            : new CellValue(null, null, EvaluationImportRowStatus.InvalidValue,
                $"Résultat « {row.Verdict} » non reconnu — attendu « Validé » ou « Non validé ».");
    }

    // Accents and casing vary with whoever typed the sheet; the words themselves do not.
    private static EvaluationOutcome? ParseVerdict(string verdict) => verdict switch
    {
        "valide" or "validee" or "v" or "oui" or "ok" or "admis" => EvaluationOutcome.Validated,
        "non valide" or "nonvalide" or "non validee" or "nv" or "non" or "refuse" or "ajourne"
            => EvaluationOutcome.NotValidated,
        _ => null,
    };

    private static List<ServicePeriod> TargetPeriods(
        InternshipAssignment assignment, EvaluationImportScope scope, int? periodNumber)
    {
        // Interrupted rotations are terminal history — they are never evaluated.
        var live = assignment.ServicePeriods.Where(p => !p.IsInterrupted);

        return scope == EvaluationImportScope.WholeStage
            ? live.OrderBy(p => p.StartDate).ToList()
            : live.Where(p => p.CohortSlotAssignment?.StageSlot.PeriodNumber == periodNumber).ToList();
    }

    private static EvaluationImportReport Summarize(IReadOnlyList<EvaluationImportRowReport> rows)
    {
        int create = rows.Count(r => r.Status == EvaluationImportRowStatus.WillCreate);
        int overwrite = rows.Count(r => r.Status == EvaluationImportRowStatus.WillOverwrite);
        int errors = rows.Count(r => r.Status.IsError());

        return new EvaluationImportReport(
            rows.Count, create, overwrite, errors,
            rows.Where(r => !r.Status.IsError()).Sum(r => r.PeriodCount),
            // All-or-nothing: one bad row refuses the import. A partial grade import is
            // unreconcilable — nobody can tell afterwards which marks made it in.
            CanApply: errors == 0 && rows.Count > 0,
            rows);
    }

    private static Resolution Fail(
        EvaluationImportRow row, string? name, EvaluationImportRowStatus status, string message,
        Guid? assignmentId = null) =>
        new(new EvaluationImportRowReport(
                row.SheetRow, Normalize(row.Cne), Normalize(row.Appogee), name, status, message, 0),
            [], assignmentId);

    private static Dictionary<string, List<InternshipAssignment>> Index(
        List<InternshipAssignment> assignments, Func<InternshipAssignment, string?> key)
    {
        var index = new Dictionary<string, List<InternshipAssignment>>();
        foreach (var a in assignments)
        {
            string? k = Normalize(key(a));
            if (k is null) continue;
            if (!index.TryGetValue(k, out var bucket)) index[k] = bucket = [];
            bucket.Add(a);
        }
        return index;
    }

    private static string StudentName(InternshipAssignment a)
    {
        var s = a.Registration.Student;
        return $"{s.FirstName ?? ""} {s.LastName ?? ""}".Trim();
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Lower-cased, trimmed and stripped of accents so "Validé" and "valide" match — the same
    /// case-insensitive discipline the search handlers follow.</summary>
    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var stripped = new string(normalized
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());

        return stripped.Normalize(System.Text.NormalizationForm.FormC);
    }
}

internal sealed record EvaluationImportPlan(
    EvaluationImportReport Report,
    IReadOnlyList<PlannedEvaluation> Work);

/// <summary>One rotation to write, with the assignment already tracked so the aggregate can do it.</summary>
internal sealed record PlannedEvaluation(
    InternshipAssignment Assignment,
    ServicePeriod Period,
    EvaluationMode Mode,
    decimal? Mark,
    EvaluationOutcome? Outcome,
    string? Comment);
