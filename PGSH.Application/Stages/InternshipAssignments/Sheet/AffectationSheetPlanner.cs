using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.AcademicYears;
using PGSH.Application.Search;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet;

/// <summary>One période the file asks for, resolved.</summary>
internal sealed record PlannedPeriod(
    int ServiceId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsDelocalized,
    string? Reason);

/// <summary>
/// One affectation the file describes — a (student, stage) unit — with every période it asks for.
/// </summary>
/// <param name="ExistingAssignmentId">
/// The affectation being rebuilt, or null when it is being created. ⚠ The apply loads this one
/// <b>tracked</b>; the planner's own reads are all detached, so the plan carries the key rather than
/// the entity.
/// </param>
internal sealed record AffectationWorkItem(
    Guid RegistrationId,
    int StageId,
    int AcademicGroupId,
    Guid? ExistingAssignmentId,
    IReadOnlyList<PlannedPeriod> Periods,
    int PeriodsDropped,
    bool IsDelocalization);

internal sealed record AffectationSheetPlan(
    int AcademicYearId,
    int LevelId,
    IReadOnlyList<AffectationWorkItem> Work,
    IReadOnlyList<(int AcademicGroupId, int StageId)> CohortsToCreate,
    AffectationSheetReport Report);

/// <summary>
/// Turns an uploaded canevas into the plan the apply executes — and into the report the operator is
/// shown. <b>One class, run by both</b>, for the same reason the bulk délocalisation has one: an
/// aperçu computed one way and a write performed another is two rules with nothing able to catch them
/// disagreeing, and here the second rule would be destroying périodes.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Every read here is flat and top-level.</b> Folding a collection into a
/// <c>Select</c> is the shape Npgsql refuses — it is what killed the macro plan with the whole suite
/// green — so the périodes are fetched by their own query keyed on the affectation and joined in
/// memory. Each query is <c>internal static</c> and named so <c>SqlTranslationTests</c> can compile
/// it without a database.</para>
///
/// <para>⚠ <b>The queries state no tracking behaviour; the planner states its own.</b>
/// <c>AsNoTracking()</c> on a reusable query reaches its host, and the apply composes nothing from
/// here — it re-loads what it mutates, tracked, by key.</para>
/// </remarks>
internal sealed class AffectationSheetPlanner(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
{
    /// <summary>
    /// How many lines the report names individually. ⚠ Needs-attention lines are ordered first, so
    /// the cap can only ever hide a line that says « rien à voir ».
    /// </summary>
    private const int MaximumReportedRows = 500;

    public async Task<Result<AffectationSheetPlan>> PlanAsync(
        int levelId,
        int? academicYearId,
        IReadOnlyList<AffectationSheetRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return Result.Failure<AffectationSheetPlan>(AffectationSheetErrors.SheetEmpty);

        var year = await yearResolver.ResolveWithLabelAsync(academicYearId, ct);
        if (year.IsFailure)
            return Result.Failure<AffectationSheetPlan>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        string? levelLabel = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == levelId)
            .Select(l => l.Label)
            .FirstOrDefaultAsync(ct);

        if (levelLabel is null)
            return Result.Failure<AffectationSheetPlan>(AffectationSheetErrors.LevelNotFound(levelId));

        var catalog = await LoadCatalogAsync(levelId, yearId, rows, ct);

        var resolved = Resolve(rows, catalog);
        var units = BuildUnits(resolved, catalog);

        var report = BuildReport(yearLabel, levelLabel, rows.Count, resolved, units, catalog);

        var work = units
            .Where(u => u.Work is not null)
            .Select(u => u.Work!)
            .ToList();

        var cohortsToCreate = work
            .Where(w => !catalog.Cohorts.ContainsKey((w.AcademicGroupId, w.StageId)))
            .Select(w => (w.AcademicGroupId, w.StageId))
            .Distinct()
            .ToList();

        return new AffectationSheetPlan(yearId, levelId, work, cohortsToCreate, report);
    }

    // ─── Reading what the file has to be resolved against ─────────────────────

    private async Task<SheetCatalog> LoadCatalogAsync(
        int levelId, int yearId, IReadOnlyList<AffectationSheetRow> rows, CancellationToken ct)
    {
        var registrations = await RegistrationsQuery(dbContext, yearId, levelId)
            .AsNoTracking()
            .ToListAsync(ct);

        // No AsNoTracking: the query projects a key, and EF tracks nothing it did not materialise as
        // an entity.
        var heldIds = (await HeldRegistrationIdsQuery(dbContext, yearId, levelId)
            .ToListAsync(ct)).ToHashSet();

        var stages = await StagesQuery(dbContext, levelId).AsNoTracking().ToListAsync(ct);
        var services = await ServicesQuery(dbContext).AsNoTracking().ToListAsync(ct);

        var registrationIds = registrations.Select(r => r.RegistrationId).ToList();
        var stageIds = stages.Select(s => s.StageId).ToList();

        var existing = await ExistingAffectationsQuery(dbContext, registrationIds, stageIds)
            .AsNoTracking()
            .ToListAsync(ct);

        // ⚠ Flat, keyed on the affectation, folded below. The alternative — a collection inside the
        // projection above — is exactly what Npgsql refuses.
        var periods = await ExistingPeriodsQuery(dbContext, registrationIds, stageIds)
            .AsNoTracking()
            .ToListAsync(ct);

        var groupIds = registrations
            .Where(r => r.AcademicGroupId is not null)
            .Select(r => r.AcademicGroupId!.Value)
            .Distinct()
            .ToList();

        var cohorts = await CohortsQuery(dbContext, groupIds, stageIds)
            .AsNoTracking()
            .ToListAsync(ct);

        // The governing text is the registration's, falling back to the student's — never the other
        // way round, and never « none means owes nothing ».
        var versionIds = registrations
            .Where(r => r.CnpnVersionId is not null)
            .Select(r => r.CnpnVersionId!.Value)
            .Distinct()
            .ToList();

        var required = await RequiredStagesQuery(dbContext, versionIds, levelId)
            .AsNoTracking()
            .ToListAsync(ct);

        var typedAppogees = Typed(rows, r => r.Appogee);
        var typedCnes = Typed(rows, r => r.Cne);

        var elsewhere = await IdentifiersInBaseQuery(dbContext, typedAppogees, typedCnes)
            .AsNoTracking()
            .ToListAsync(ct);

        var periodsByAssignment = periods
            .GroupBy(p => p.AssignmentId)
            .ToDictionary(g => g.Key, IReadOnlyList<ExistingPeriod> (g) => g.ToList());

        return new SheetCatalog(
            ByAppogee: registrations
                .Where(r => !string.IsNullOrWhiteSpace(r.Appogee))
                .GroupBy(r => SearchTerms.Fold(r.Appogee!))
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single()),
            ByCne: registrations
                .Where(r => !string.IsNullOrWhiteSpace(r.Cne))
                .GroupBy(r => SearchTerms.Fold(r.Cne!))
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single()),
            Registrations: registrations,
            HeldRegistrationIds: heldIds,
            Stages: stages,
            Services: services,
            Existing: existing.GroupBy(a => (a.RegistrationId, a.StageId))
                              .ToDictionary(g => g.Key, IReadOnlyList<ExistingAffectation> (g) => g.ToList()),
            PeriodsByAssignment: periodsByAssignment,
            Cohorts: cohorts.ToDictionary(c => (c.AcademicGroupId, c.StageId), c => c.CohortId),
            RequiredStages: required.GroupBy(r => r.CnpnVersionId)
                                    .ToDictionary(g => g.Key, g => g.Select(r => r.StageId).ToHashSet()),
            IdentifiersInBase: elsewhere
                .SelectMany(s => new[] { s.Appogee, s.Cne })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => SearchTerms.Fold(value!))
                .ToHashSet());
    }

    /// <summary>The distinct non-blank values of one identifier column, as the file typed them.</summary>
    private static List<string> Typed(
        IReadOnlyList<AffectationSheetRow> rows, Func<AffectationSheetRow, string?> column) =>
        rows.Select(column)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct()
            .ToList();

    // ─── Line by line ─────────────────────────────────────────────────────────

    private static List<ResolvedRow> Resolve(
        IReadOnlyList<AffectationSheetRow> rows, SheetCatalog catalog)
    {
        var resolved = new List<ResolvedRow>(rows.Count);
        var seen = new HashSet<(Guid, int, int, DateOnly, DateOnly)>();

        foreach (var row in rows)
        {
            string? identifier = Prefer(row.Appogee, row.Cne);

            // ⚠ **Asked first, before the line is even matched to anybody.** A line whose planning half
            // is blank asks for nothing, and nothing it says about a student can therefore be wrong.
            // Measured on the live base 13/09/2026: the 4ᵉ année Pharmacie holds 232 inscriptions and
            // **no roster at all** — un-cut promotions are the ordinary state, not an edge case — so
            // asking « est-il dans un groupe ? » first turned an untouched canvas into 232 errors and
            // refused the whole file. The operator had touched none of those lines. The stage is still
            // resolved, for the report only: it groups the breakdown, and it cannot refuse here.
            if (IsUnplanned(row))
            {
                resolved.Add(ResolvedRow.Refused(
                    row, identifier, Find(catalog, row), AffectationSheetRowStatus.NotPlanned,
                    MatchOne(catalog.Stages, s => s.StageName, row.StageName)));
                continue;
            }

            var registration = Find(catalog, row);
            if (registration is null)
            {
                // ⚠ The two are told apart, and it is worth the extra read. A code nobody carries is a
                // typo; a code carried by somebody registered elsewhere means the file was cut for
                // another promotion — and « aucun étudiant ne porte cet identifiant » sends the
                // operator looking for a student who is right there in the base.
                var status = string.IsNullOrWhiteSpace(identifier)
                    ? AffectationSheetRowStatus.NoIdentifier
                    : ExistsElsewhere(catalog, row)
                        ? AffectationSheetRowStatus.WrongPromotion
                        : AffectationSheetRowStatus.StudentNotFound;

                resolved.Add(ResolvedRow.Refused(row, identifier, null, status));
                continue;
            }

            if (catalog.HeldRegistrationIds.Contains(registration.RegistrationId))
            {
                resolved.Add(ResolvedRow.Refused(
                    row, identifier, registration, AffectationSheetRowStatus.OnHold));
                continue;
            }

            if (registration.AcademicGroupId is not { } groupId)
            {
                resolved.Add(ResolvedRow.Refused(
                    row, identifier, registration, AffectationSheetRowStatus.NoRoster));
                continue;
            }

            var stageMatches = Match(catalog.Stages, s => s.StageName, row.StageName);
            if (stageMatches.Count != 1)
            {
                resolved.Add(ResolvedRow.Refused(row, identifier, registration,
                    stageMatches.Count == 0
                        ? AffectationSheetRowStatus.UnknownStage
                        : AffectationSheetRowStatus.AmbiguousStage));
                continue;
            }

            var stage = stageMatches[0];

            var serviceMatches = Match(catalog.Services, s => s.ServiceName, row.ServiceName);
            if (serviceMatches.Count > 1 && !string.IsNullOrWhiteSpace(row.HospitalName))
            {
                string hospital = SearchTerms.Fold(row.HospitalName);
                serviceMatches = serviceMatches
                    .Where(s => s.HospitalName is not null
                             && SearchTerms.Fold(s.HospitalName) == hospital)
                    .ToList();
            }

            if (serviceMatches.Count != 1)
            {
                resolved.Add(ResolvedRow.Refused(row, identifier, registration,
                    serviceMatches.Count == 0
                        ? AffectationSheetRowStatus.UnknownService
                        : AffectationSheetRowStatus.AmbiguousService,
                    stage));
                continue;
            }

            var service = serviceMatches[0];

            if (row.StartDate is not { } start || row.EndDate is not { } end)
            {
                resolved.Add(ResolvedRow.Refused(row, identifier, registration,
                    AffectationSheetRowStatus.MissingDates, stage, service));
                continue;
            }

            if (end < start)
            {
                resolved.Add(ResolvedRow.Refused(row, identifier, registration,
                    AffectationSheetRowStatus.BadDateOrder, stage, service));
                continue;
            }

            string? reason = string.IsNullOrWhiteSpace(row.DelocalizationReason)
                ? null
                : row.DelocalizationReason.Trim();

            // Either half alone is a half-stated délocalisation: an external service with no motif
            // leaves the only trace of that stage blank, and a motif on an in-faculty service says
            // two things at once.
            if (service.IsExternal != (reason is not null))
            {
                resolved.Add(ResolvedRow.Refused(row, identifier, registration,
                    AffectationSheetRowStatus.DelocalizationWithoutReason, stage, service));
                continue;
            }

            if (!seen.Add((registration.RegistrationId, stage.StageId, service.ServiceId, start, end)))
            {
                resolved.Add(ResolvedRow.Refused(row, identifier, registration,
                    AffectationSheetRowStatus.DuplicateRow, stage, service));
                continue;
            }

            resolved.Add(new ResolvedRow
            {
                Row = row,
                Identifier = identifier,
                Registration = registration,
                Stage = stage,
                Service = service,
                AcademicGroupId = groupId,
                Period = new PlannedPeriod(service.ServiceId, start, end, service.IsExternal, reason),
                OutsideCnpn = IsOutsideCnpn(catalog, registration, stage.StageId),
            });
        }

        return resolved;
    }

    /// <summary>
    /// The planning half of the line is entirely blank — the canvas as it came out. ⚠ <b>Entirely</b>:
    /// a service with no dates, or dates with no service, is somebody who started filling the line and
    /// stopped, and that is a refusal rather than a skip.
    /// </summary>
    private static bool IsUnplanned(AffectationSheetRow row) =>
        string.IsNullOrWhiteSpace(row.ServiceName)
        && row.StartDate is null
        && row.EndDate is null
        && string.IsNullOrWhiteSpace(row.DelocalizationReason);

    /// <summary>
    /// ⚠ Null means « no text on record », which is not « owes nothing ». ~2 200 enrolled students
    /// carry no stamp at all, and reading that as an empty requirement set would flag every one of
    /// their lines.
    /// </summary>
    private static bool IsOutsideCnpn(SheetCatalog catalog, SheetRegistration registration, int stageId) =>
        registration.CnpnVersionId is { } versionId
        && catalog.RequiredStages.TryGetValue(versionId, out var required)
        && required.Count > 0
        && !required.Contains(stageId);

    // ─── Folding the lines back into affectations ─────────────────────────────

    private static List<ResolvedUnit> BuildUnits(List<ResolvedRow> rows, SheetCatalog catalog)
    {
        var units = new List<ResolvedUnit>();

        foreach (var group in rows
            .Where(r => r.Status is null)
            .GroupBy(r => (r.Registration!.RegistrationId, r.Stage!.StageId)))
        {
            var members = group.OrderBy(r => r.Period!.StartDate).ToList();
            var first = members[0];

            var existing = catalog.Existing.TryGetValue(group.Key, out var found) ? found : [];

            if (existing.Count > 1)
            {
                Refuse(members, AffectationSheetRowStatus.AmbiguousAffectation);
                continue;
            }

            var periods = members.Select(r => r.Period!).ToList();
            bool delocalized = periods.Any(p => p.IsDelocalized);

            // Delocalize() replaces the whole rotation with one external période, so a second line
            // would be written and immediately discarded by the first — silently.
            if (delocalized && periods.Count > 1)
            {
                Refuse(members, AffectationSheetRowStatus.MalformedDelocalization);
                continue;
            }

            var assignment = existing.Count == 1 ? existing[0] : null;
            var current = assignment is not null
                       && catalog.PeriodsByAssignment.TryGetValue(assignment.AssignmentId, out var held)
                ? held
                : [];

            // ⚠ Asked before the mark is: an identical file describes, correctly, the very rotation
            // those marks were given for, and a promotion mid-évaluation must stay re-uploadable.
            if (assignment is not null && SaysTheSameThing(current, periods))
            {
                units.Add(new ResolvedUnit(members, AffectationSheetRowStatus.Unchanged, null));
                continue;
            }

            if (current.Any(p => p.HasEvaluation))
            {
                Refuse(members, AffectationSheetRowStatus.AlreadyMarked);
                continue;
            }

            var status = delocalized ? AffectationSheetRowStatus.WillDelocalize
                       : assignment is null ? AffectationSheetRowStatus.WillCreate
                       : AffectationSheetRowStatus.WillReplace;

            units.Add(new ResolvedUnit(members, status, new AffectationWorkItem(
                first.Registration!.RegistrationId,
                first.Stage!.StageId,
                first.AcademicGroupId,
                assignment?.AssignmentId,
                periods,
                current.Count,
                delocalized)));
        }

        return units;

        static void Refuse(List<ResolvedRow> members, AffectationSheetRowStatus status)
        {
            foreach (var member in members)
                member.Status = status;
        }
    }

    /// <summary>
    /// Whether what is on record and what the file asks for are the same rotation — same services,
    /// same windows, same count, same provenance.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>An interrupted période counts.</b> It is terminal — a rotation cut short by a forced
    /// transfer — and treating it as absent would make every such affectation look like it needed
    /// rebuilding, which would then destroy the record of the interruption.
    /// </remarks>
    private static bool SaysTheSameThing(
        IReadOnlyList<ExistingPeriod> current, IReadOnlyList<PlannedPeriod> planned)
    {
        if (current.Count != planned.Count)
            return false;

        var left = current
            .Select(p => (p.ServiceId, p.StartDate, p.EndDate, p.IsDelocalized))
            .OrderBy(p => p.StartDate).ThenBy(p => p.ServiceId)
            .ToList();

        var right = planned
            .Select(p => (p.ServiceId, p.StartDate, p.EndDate, p.IsDelocalized))
            .OrderBy(p => p.StartDate).ThenBy(p => p.ServiceId)
            .ToList();

        return left.SequenceEqual(right);
    }

    // ─── The report ───────────────────────────────────────────────────────────

    private static AffectationSheetReport BuildReport(
        string yearLabel,
        string levelLabel,
        int totalRows,
        List<ResolvedRow> rows,
        List<ResolvedUnit> units,
        SheetCatalog catalog)
    {
        foreach (var unit in units)
            foreach (var member in unit.Members)
                member.Status ??= unit.Status;

        int errors = rows.Count(r => r.Status!.Value.IsError());

        var work = units.Where(u => u.Work is not null).Select(u => u.Work!).ToList();

        var droppedAssignmentIds = work
            .Where(w => w.ExistingAssignmentId is not null)
            .Select(w => w.ExistingAssignmentId!.Value)
            .ToList();

        int publishedDropped = droppedAssignmentIds
            .Where(catalog.PeriodsByAssignment.ContainsKey)
            .SelectMany(id => catalog.PeriodsByAssignment[id])
            .Count(p => p.FromGrid);

        var coveredRegistrations = rows
            .Where(r => r.Registration is not null)
            .Select(r => r.Registration!.RegistrationId)
            .ToHashSet();

        var byStage = rows
            .Where(r => r.Stage is not null)
            .GroupBy(r => r.Stage!.StageName)
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new AffectationSheetStageBreakdown(
                g.Key,
                g.Count(),
                g.Select(r => (r.Registration!.RegistrationId, r.Stage!.StageId)).Distinct().Count(),
                g.Count(r => r.Status == AffectationSheetRowStatus.WillCreate),
                g.Count(r => r.Status == AffectationSheetRowStatus.WillReplace),
                g.Count(r => r.Status == AffectationSheetRowStatus.WillDelocalize),
                g.Count(r => r.Status == AffectationSheetRowStatus.Unchanged),
                g.Count(r => r.Status!.Value.IsError())))
            .ToList();

        var reported = rows
            .OrderByDescending(r => r.Status!.Value.NeedsAttention())
            .ThenBy(r => r.Row.SheetRow)
            .Take(MaximumReportedRows)
            .Select(r => new AffectationSheetRowReport(
                r.Row.SheetRow,
                r.Identifier,
                r.Registration is null ? null : $"{r.Registration.FirstName} {r.Registration.LastName}".Trim(),
                r.Stage?.StageName ?? r.Row.StageName,
                r.Service?.ServiceName ?? r.Row.ServiceName,
                r.Status!.Value,
                r.OutsideCnpn,
                Describe(r)))
            .ToList();

        int notCovered = catalog.Registrations.Count(r => !coveredRegistrations.Contains(r.RegistrationId));
        int cohortsToCreate = work
            .Where(w => !catalog.Cohorts.ContainsKey((w.AcademicGroupId, w.StageId)))
            .Select(w => (w.AcademicGroupId, w.StageId))
            .Distinct()
            .Count();

        int periodsToDrop = work.Sum(w => w.PeriodsDropped);

        return new AffectationSheetReport(
            yearLabel,
            levelLabel,
            totalRows,
            work.Count,
            units.Count(u => u.Status == AffectationSheetRowStatus.WillCreate),
            units.Count(u => u.Status == AffectationSheetRowStatus.WillReplace),
            units.Count(u => u.Status == AffectationSheetRowStatus.WillDelocalize),
            units.Count(u => u.Status == AffectationSheetRowStatus.Unchanged),
            rows.Count(r => r.Status == AffectationSheetRowStatus.NotPlanned),
            work.Sum(w => w.Periods.Count),
            periodsToDrop,
            publishedDropped,
            cohortsToCreate,
            coveredRegistrations.Count,
            notCovered,
            rows.Count(r => r.OutsideCnpn),
            errors,
            // ⚠ A file that changes nothing still applies — and writes nothing. Refusing it would make
            // « renvoie le fichier corrigé » stop working the moment the correction turns out to have
            // already been applied, which is the one moment somebody needs to be sure.
            CanApply: errors == 0,
            byStage,
            reported,
            RowsTruncated: rows.Count > MaximumReportedRows,
            Notes: BuildNotes(work.Count, periodsToDrop, publishedDropped, cohortsToCreate, notCovered,
                              rows.Count(r => r.Status == AffectationSheetRowStatus.NotPlanned), errors));
    }

    /// <summary>
    /// What the numbers do not say, in words.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Every note here is conditional.</b> A warning that fires whatever the data says is noise,
    /// and noise is dismissed — which puts the real one out of sight. The hors-grille note is the
    /// exception and is unconditional for the opposite reason: it is true of every période this act
    /// writes, and it is the one thing the figures cannot show.
    /// </remarks>
    private static List<string> BuildNotes(
        int work, int periodsToDrop, int publishedDropped, int cohortsToCreate, int notCovered,
        int notPlanned, int errors)
    {
        var notes = new List<string>();

        if (errors > 0)
            notes.Add($"{errors} ligne(s) empêchent l'application du fichier : rien ne sera écrit tant "
                    + "qu'elles ne sont pas corrigées. Elles sont listées en tête du rapport.");

        if (work > 0)
            notes.Add("Les périodes créées ici sont hors grille : elles apparaissent dans le dossier de "
                    + "l'étudiant et sur la page du service, mais pas dans le planning, et elles ne "
                    + "comptent pas dans la charge que la grille affiche.");

        if (publishedDropped > 0)
            notes.Add($"{publishedDropped} des {periodsToDrop} période(s) supprimées viennent de la grille : "
                    + "une répartition publiée sera remplacée par des périodes hors grille, et republier "
                    + "ne la rétablira pas.");

        if (cohortsToCreate > 0)
            notes.Add($"{cohortsToCreate} cohorte(s) seront créées parce que le fichier les nomme et "
                    + "qu'elles n'existent pas encore.");

        if (notPlanned > 0)
            notes.Add($"{notPlanned} ligne(s) sont restées en blanc : ces stages ne sont pas planifiés "
                    + "et le fichier ne les touche pas. C'est normal si vous ne planifiez qu'une partie "
                    + "de la promotion.");

        if (notCovered > 0)
            notes.Add($"{notCovered} inscription(s) de la promotion ne sont nommées nulle part dans le "
                    + "fichier. Elles ne sont pas touchées — un canevas partiel est un usage normal — "
                    + "mais si vous attendiez la promotion entière, il en manque.");

        return notes;
    }

    private static string Describe(ResolvedRow row) => row.Status switch
    {
        AffectationSheetRowStatus.WillCreate =>
            "Affectation créée avec cette période.",
        AffectationSheetRowStatus.WillReplace =>
            "Affectation existante : ses périodes seront supprimées et réécrites depuis le fichier.",
        AffectationSheetRowStatus.WillDelocalize =>
            "Stage servi hors faculté : une période unique, déjà commencée et terminée, avec le motif.",
        AffectationSheetRowStatus.Unchanged =>
            "L'affectation dit déjà exactement cela ; rien à écrire.",
        AffectationSheetRowStatus.NotPlanned =>
            "Ligne laissée en blanc : ce stage n'est pas encore planifié pour cet étudiant.",
        AffectationSheetRowStatus.NoIdentifier =>
            "Ni numéro Apogée ni CNE : la ligne ne désigne personne.",
        AffectationSheetRowStatus.DuplicateRow =>
            "Cette période figure déjà à l'identique sur une autre ligne.",
        AffectationSheetRowStatus.StudentNotFound =>
            "Aucun étudiant de cette promotion ne porte cet identifiant.",
        AffectationSheetRowStatus.WrongPromotion =>
            "L'étudiant existe mais n'est pas inscrit dans la promotion du fichier.",
        AffectationSheetRowStatus.OnHold =>
            "Inscription signalée : la planification doit la laisser tranquille. Levez le signalement d'abord.",
        AffectationSheetRowStatus.NoRoster =>
            "L'étudiant n'est dans aucun groupe : il faut découper la promotion avant de l'affecter.",
        AffectationSheetRowStatus.UnknownStage =>
            "Aucun stage de ce niveau ne porte ce nom.",
        AffectationSheetRowStatus.AmbiguousStage =>
            "Deux stages du niveau portent ce nom : la ligne n'en désigne aucun.",
        AffectationSheetRowStatus.UnknownService =>
            "Aucun service ne porte ce nom.",
        AffectationSheetRowStatus.AmbiguousService =>
            "Plusieurs services portent ce nom ; renseignez la colonne « Hôpital » pour les distinguer.",
        AffectationSheetRowStatus.MissingDates =>
            "Une des deux dates est vide ou illisible.",
        AffectationSheetRowStatus.BadDateOrder =>
            "La date de fin est antérieure à la date de début.",
        AffectationSheetRowStatus.AlreadyMarked =>
            "Ce stage porte déjà une note et le fichier en décrit un autre : une note ne se remplace pas ici.",
        AffectationSheetRowStatus.DelocalizationWithoutReason =>
            "Une délocalisation se déclare des deux côtés : un service externe et un motif. Il en manque un.",
        AffectationSheetRowStatus.MalformedDelocalization =>
            "Un stage délocalisé tient sur une seule ligne et ne se mélange pas à des périodes internes.",
        AffectationSheetRowStatus.AmbiguousAffectation =>
            "L'étudiant porte plusieurs affectations pour ce stage (un rattrapage) : la ligne ne dit pas laquelle.",
        _ => string.Empty,
    };

    // ─── Matching ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Does somebody in the base carry this identifier, outside the promotion the file was cut for?
    /// </summary>
    /// <remarks>
    /// ⚠ Only ever decides <i>which refusal</i> is shown — both refuse — so the exact match this rests
    /// on is enough. It is the narrower of the two comparisons the appariement uses: the folded lookup
    /// finds the student, and this one only has to recognise him again.
    /// </remarks>
    private static bool ExistsElsewhere(SheetCatalog catalog, AffectationSheetRow row) =>
        (!string.IsNullOrWhiteSpace(row.Appogee)
            && catalog.IdentifiersInBase.Contains(SearchTerms.Fold(row.Appogee)))
        || (!string.IsNullOrWhiteSpace(row.Cne)
            && catalog.IdentifiersInBase.Contains(SearchTerms.Fold(row.Cne)));

    private static SheetRegistration? Find(SheetCatalog catalog, AffectationSheetRow row)
    {
        // Apogée first: it is the identifier this base always carries, and the one the faculty's own
        // documents key on. The CNE is absent on nearly half the roll and is tried second rather than
        // not at all, because a canvas edited by hand may well have kept only it.
        if (!string.IsNullOrWhiteSpace(row.Appogee)
            && catalog.ByAppogee.TryGetValue(SearchTerms.Fold(row.Appogee), out var byAppogee))
            return byAppogee;

        if (!string.IsNullOrWhiteSpace(row.Cne)
            && catalog.ByCne.TryGetValue(SearchTerms.Fold(row.Cne), out var byCne))
            return byCne;

        return null;
    }

    /// <summary>The one match, or null — for a lookup that reports rather than refuses.</summary>
    private static T? MatchOne<T>(IReadOnlyList<T> candidates, Func<T, string?> name, string? typed)
        where T : class
    {
        var matches = Match(candidates, name, typed);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static List<T> Match<T>(IReadOnlyList<T> candidates, Func<T, string?> name, string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return [];

        string folded = SearchTerms.Fold(typed);
        return candidates.Where(c => name(c) is not null && SearchTerms.Fold(name(c)!) == folded).ToList();
    }

    private static string? Prefer(string? first, string? second) =>
        string.IsNullOrWhiteSpace(first) ? (string.IsNullOrWhiteSpace(second) ? null : second.Trim())
                                         : first.Trim();

    // ─── The queries, named so they can be compiled without a database ────────

    internal static IQueryable<SheetRegistration> RegistrationsQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId) =>
        dbContext.Registrations
            .Where(r => r.AcademicYearId == academicYearId && r.LevelId == levelId)
            .Select(r => new SheetRegistration(
                r.Id,
                r.Student.LastName,
                r.Student.FirstName,
                r.Student.Appogee,
                r.Student.CNE,
                r.AcademicGroupId,
                r.CnpnVersionId ?? r.Student.CnpnVersionId));

    /// <summary>
    /// ⚠ Through <c>RegistrationHoldPolicy</c>'s expression, in a <c>Where</c> — the one place a
    /// collection may be aggregated without meeting the shape Npgsql refuses. Restating the rule here
    /// would be a sixth copy of « qui la planification doit laisser tranquille ».
    /// </summary>
    internal static IQueryable<Guid> HeldRegistrationIdsQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId) =>
        dbContext.Registrations
            .Where(r => r.AcademicYearId == academicYearId && r.LevelId == levelId)
            .Where(RegistrationHoldPolicy.OnHold)
            .Select(r => r.Id);

    internal static IQueryable<SheetStage> StagesQuery(IApplicationDbContext dbContext, int levelId) =>
        dbContext.Stages
            .Where(s => s.LevelId == levelId)
            .Select(s => new SheetStage(s.Id, s.Name));

    internal static IQueryable<SheetService> ServicesQuery(IApplicationDbContext dbContext) =>
        dbContext.Services
            .Select(s => new SheetService(s.Id, s.Name, s.Hospital.Name, s.IsExternal));

    internal static IQueryable<ExistingAffectation> ExistingAffectationsQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> registrationIds,
        IReadOnlyCollection<int> stageIds) =>
        dbContext.InternshipAssignments
            .Where(a => registrationIds.Contains(a.RegistrationId)
                     && stageIds.Contains(a.Cohort.StageId))
            .Select(a => new ExistingAffectation(a.Id, a.RegistrationId, a.Cohort.StageId));

    /// <summary>
    /// Every période of those affectations, flat. ⚠ Not folded into the query above: a collection in a
    /// projection is the shape the provider refuses, and <c>HasEvaluation</c> — which decides whether
    /// this act may touch the affectation at all — would come back <c>false</c> from an un-Included
    /// navigation, i.e. « rien à perdre » on a stage that carries a mark.
    /// </summary>
    internal static IQueryable<ExistingPeriod> ExistingPeriodsQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> registrationIds,
        IReadOnlyCollection<int> stageIds) =>
        dbContext.ServicePeriods
            .Where(p => registrationIds.Contains(p.InternshipAssignment.RegistrationId)
                     && stageIds.Contains(p.InternshipAssignment.Cohort.StageId))
            .Select(p => new ExistingPeriod(
                p.InternshipAssignmentId,
                p.ServiceId,
                p.StartDate,
                p.EndDate,
                p.IsDelocalized,
                p.CohortSlotAssignmentId != null,
                p.Evaluation != null));

    internal static IQueryable<SheetCohort> CohortsQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<int> academicGroupIds,
        IReadOnlyCollection<int> stageIds) =>
        dbContext.Cohorts
            .Where(c => academicGroupIds.Contains(c.AcademicGroupId) && stageIds.Contains(c.StageId))
            .Select(c => new SheetCohort(c.Id, c.AcademicGroupId, c.StageId));

    /// <summary>
    /// The identifiers of this file that exist <b>somewhere</b> in the base, promotion or not. Feeds
    /// the <c>WrongPromotion</c> / <c>StudentNotFound</c> split and nothing else.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>s.CNE != null</c> is not redundant in front of the <c>Contains</c>. In SQL
    /// <c>NULL IN (…)</c> is unknown rather than false, and the in-memory provider throws on the same
    /// expression instead of answering « not true » — the column is absent on 46 % of the roll, so
    /// this is the ordinary row, not the edge one.
    /// </remarks>
    internal static IQueryable<SheetIdentifier> IdentifiersInBaseQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<string> appogees,
        IReadOnlyCollection<string> cnes) =>
        dbContext.Students
            .Where(s => appogees.Contains(s.Appogee) || (s.CNE != null && cnes.Contains(s.CNE)))
            .Select(s => new SheetIdentifier(s.Appogee, s.CNE));

    internal static IQueryable<RequiredStage> RequiredStagesQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<int> cnpnVersionIds,
        int levelId) =>
        dbContext.CurriculumStages
            .Where(cs => cs.Curriculum.LevelId == levelId
                      && cnpnVersionIds.Contains(cs.Curriculum.CnpnVersionId))
            .Select(cs => new RequiredStage(cs.Curriculum.CnpnVersionId, cs.StageId));

    // ─── What the planner carries between its own steps ───────────────────────

    private sealed record SheetCatalog(
        IReadOnlyDictionary<string, SheetRegistration> ByAppogee,
        IReadOnlyDictionary<string, SheetRegistration> ByCne,
        IReadOnlyList<SheetRegistration> Registrations,
        IReadOnlySet<Guid> HeldRegistrationIds,
        IReadOnlyList<SheetStage> Stages,
        IReadOnlyList<SheetService> Services,
        IReadOnlyDictionary<(Guid RegistrationId, int StageId), IReadOnlyList<ExistingAffectation>> Existing,
        IReadOnlyDictionary<Guid, IReadOnlyList<ExistingPeriod>> PeriodsByAssignment,
        IReadOnlyDictionary<(int AcademicGroupId, int StageId), int> Cohorts,
        IReadOnlyDictionary<int, HashSet<int>> RequiredStages,
        IReadOnlySet<string> IdentifiersInBase);

    /// <summary>
    /// One line as the planner has understood it so far. A class rather than a record because
    /// <see cref="Status"/> is genuinely settled in two passes — line by line, then again once the
    /// lines of one affectation are read together — and a value type pretending otherwise would just
    /// make the second pass rebuild the list.
    /// </summary>
    private sealed class ResolvedRow
    {
        public required AffectationSheetRow Row { get; init; }
        public required string? Identifier { get; init; }
        public SheetRegistration? Registration { get; init; }
        public SheetStage? Stage { get; init; }
        public SheetService? Service { get; init; }
        public int AcademicGroupId { get; init; }
        public PlannedPeriod? Period { get; init; }
        public bool OutsideCnpn { get; init; }

        public AffectationSheetRowStatus? Status { get; set; }

        public static ResolvedRow Refused(
            AffectationSheetRow row,
            string? identifier,
            SheetRegistration? registration,
            AffectationSheetRowStatus status,
            SheetStage? stage = null,
            SheetService? service = null) =>
            new()
            {
                Row = row,
                Identifier = identifier,
                Registration = registration,
                Stage = stage,
                Service = service,
                Status = status,
            };
    }

    private sealed record ResolvedUnit(
        List<ResolvedRow> Members,
        AffectationSheetRowStatus Status,
        AffectationWorkItem? Work);
}

internal sealed record SheetRegistration(
    Guid RegistrationId,
    string LastName,
    string FirstName,
    string Appogee,
    string? Cne,
    int? AcademicGroupId,
    int? CnpnVersionId);

internal sealed record SheetStage(int StageId, string StageName);

internal sealed record SheetService(int ServiceId, string ServiceName, string? HospitalName, bool IsExternal);

internal sealed record SheetCohort(int CohortId, int AcademicGroupId, int StageId);

internal sealed record SheetIdentifier(string Appogee, string? Cne);

internal sealed record ExistingAffectation(Guid AssignmentId, Guid RegistrationId, int StageId);

internal sealed record ExistingPeriod(
    Guid AssignmentId,
    int ServiceId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsDelocalized,
    bool FromGrid,
    bool HasEvaluation);

internal sealed record RequiredStage(int CnpnVersionId, int StageId);
