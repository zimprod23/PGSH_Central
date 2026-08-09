using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Deliberation;

/// <summary>
/// Turns an uploaded déliberation sheet into the exact set of writes it would perform, and reports
/// every row that cannot be written and why.
///
/// Preview and apply both run this and nothing else, so the dry run the user confirmed is literally
/// the plan that executes — the same guarantee the evaluation import and the CNPN targeting make, for
/// the same reason. It loads tracked registrations in both cases; the preview simply never saves.
///
/// One sheet is one promotion: (academic year, level). That is how a jury sits, and it is also what
/// makes the identifier index mean something — a CNE is unique within a promotion, and matching
/// across years turns a legitimate row into an ambiguous one.
/// </summary>
internal sealed class DeliberationPlanner(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
{
    public async Task<Result<DeliberationPlan>> PlanAsync(
        int levelId,
        int? academicYearId,
        IReadOnlyList<DeliberationRow> rows,
        CancellationToken ct)
    {
        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == levelId)
            .Select(l => new { l.Id, l.Label, l.Year, l.AcademicProgram })
            .FirstOrDefaultAsync(ct);

        if (level is null)
            return Result.Failure<DeliberationPlan>(RegistrationErrors.MissingLevel);

        var year = await yearResolver.ResolveWithLabelAsync(academicYearId, ct);
        if (year.IsFailure)
            return Result.Failure<DeliberationPlan>(year.Error);

        (int yearId, string yearLabel) = year.Value;
        string levelLabel = level.Label ?? $"Année {level.Year} — {level.AcademicProgram}";

        // The promotion itself: one registration per student for this (year, level). Tracked, because
        // the apply writes through RecordYearOutcome on these very rows.
        var registrations = await dbContext.Registrations
            .Include(r => r.Student)
            .Where(r => r.AcademicYearId == yearId && r.LevelId == levelId)
            .ToListAsync(ct);

        if (registrations.Count == 0)
            return Result.Failure<DeliberationPlan>(
                DeliberationErrors.PromotionHasNoStudents(levelLabel, yearLabel));

        var finalYears = await FinalYearByStudentAsync(registrations, ct);
        var unvalidated = await StudentsWithUnvalidatedStagesAsync(registrations, ct);

        var byCne = Index(registrations, r => r.Student?.CNE);
        var byAppogee = Index(registrations, r => r.Student?.Appogee);

        var reports = new List<DeliberationRowReport>(rows.Count);
        var work = new List<PlannedOutcome>();
        var seen = new HashSet<Guid>();

        foreach (var row in rows)
        {
            var resolved = Resolve(row, byCne, byAppogee, seen, level.Year, finalYears, unvalidated);
            reports.Add(resolved.Report);
            if (resolved.Work is { } planned) work.Add(planned);
            if (resolved.RegistrationId is { } id) seen.Add(id);
        }

        var report = Summarize(yearLabel, levelLabel, reports, registrations.Count - seen.Count);
        return new DeliberationPlan(report, work);
    }

    private sealed record Resolution(
        DeliberationRowReport Report,
        PlannedOutcome? Work,
        Guid? RegistrationId);

    private static Resolution Resolve(
        DeliberationRow row,
        IReadOnlyDictionary<string, List<Registration>> byCne,
        IReadOnlyDictionary<string, List<Registration>> byAppogee,
        IReadOnlySet<Guid> seen,
        int levelYear,
        IReadOnlyDictionary<Guid, int> finalYears,
        IReadOnlySet<Guid> unvalidated)
    {
        string? cne = Normalize(row.Cne);
        string? appogee = Normalize(row.Appogee);

        if (cne is null && appogee is null)
            return Fail(row, null, DeliberationRowStatus.NoIdentifier,
                "Ni CNE ni numéro Apogée — la ligne ne désigne aucun étudiant.");

        var byCneMatch = cne is not null ? byCne.GetValueOrDefault(cne) : null;
        var byAppogeeMatch = appogee is not null ? byAppogee.GetValueOrDefault(appogee) : null;

        // Both identifiers given and pointing at different people: one of the two cells is mistyped,
        // and picking either would close the wrong student's year.
        if (byCneMatch is not null && byAppogeeMatch is not null
            && !byCneMatch.Select(r => r.Id).ToHashSet().SetEquals(byAppogeeMatch.Select(r => r.Id)))
            return Fail(row, null, DeliberationRowStatus.InvalidDecision,
                "Le CNE et le numéro Apogée de cette ligne désignent deux étudiants différents.");

        var matches = byCneMatch ?? byAppogeeMatch;
        if (matches is null)
            return Fail(row, null, DeliberationRowStatus.UnknownStudent,
                "Aucun étudiant de cette promotion ne porte cet identifiant.");

        // A student holds at most one registration per year (unique index), so this cannot normally
        // happen — but two students sharing an identifier can, and it is exactly the case where
        // guessing writes a verdict onto the wrong file.
        if (matches.Count > 1)
            return Fail(row, StudentName(matches[0]), DeliberationRowStatus.NotInPromotion,
                "Cet identifiant est porté par plusieurs inscriptions de la promotion — à traiter individuellement.");

        var registration = matches[0];
        string name = StudentName(registration);

        if (seen.Contains(registration.Id))
            return Fail(row, name, DeliberationRowStatus.DuplicateStudent,
                "Cet étudiant apparaît déjà plus haut dans le fichier.", registration.Id);

        string? decision = Normalize(row.Decision);
        if (decision is null)
            return Fail(row, name, DeliberationRowStatus.MissingDecision,
                "Colonne Décision vide.", registration.Id);

        if (ParseDecision(decision) is not { } outcome)
            return Fail(row, name, DeliberationRowStatus.InvalidDecision,
                $"Décision « {row.Decision} » non reconnue — attendu Admis, Redoublant, Exclu, Diplômé ou Abandon.",
                registration.Id);

        // « Diplômé » is the end of a course of study, so it has to be the end of one. Where the
        // student carries no CNPN stamp the check stands aside rather than refusing: ~2,200 stamps are
        // inferred and 19 students have none at all, and blocking a whole promotion on that would make
        // the feature unusable. Same standing-aside rule as CohortProvisioner's.
        if (outcome == RegistrationStatus.Graduated
            && finalYears.TryGetValue(registration.StudentId, out int totalYears)
            && levelYear != totalYears)
            return Fail(row, name, DeliberationRowStatus.NotAFinalYear,
                $"« Diplômé » sur une {levelYear}ᵉ année alors que le CNPN de cet étudiant en compte {totalYears}.",
                registration.Id);

        bool replaces = registration.OutcomeSource is not null;
        string? motif = Trim(row.Motif);

        // FailureReasons is where a verdict's motif lives. On a favourable decision it has nothing to
        // qualify, so it is dropped — and the row says so, because silently discarding what someone
        // typed is how a user learns not to trust the preview.
        bool motifDropped = motif is not null && !IsAdverse(outcome);

        string message = (replaces, motifDropped) switch
        {
            (true, true) => "Remplace la décision déjà enregistrée. Motif ignoré (décision favorable).",
            (true, false) => "Remplace la décision déjà enregistrée.",
            (false, true) => "Motif ignoré (décision favorable).",
            (false, false) => "Décision enregistrée.",
        };

        return new Resolution(
            new DeliberationRowReport(
                row.SheetRow, Trim(row.Cne), Trim(row.Appogee), name,
                replaces ? DeliberationRowStatus.WillReplace : DeliberationRowStatus.WillRecord,
                outcome, message,
                HasUnvalidatedStages: IsFavourable(outcome) && unvalidated.Contains(registration.StudentId)),
            new PlannedOutcome(registration, outcome, motifDropped ? null : motif),
            registration.Id);
    }

    /// <summary>
    /// How many years the student's own CNPN lasts, for the students of this promotion that carry a
    /// stamp. Absent from the dictionary means "no text on record" — see the standing-aside rule above.
    /// </summary>
    private async Task<Dictionary<Guid, int>> FinalYearByStudentAsync(
        List<Registration> registrations, CancellationToken ct)
    {
        var studentIds = registrations.Select(r => r.StudentId).ToList();

        var stamped = await dbContext.Students
            .AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && s.CnpnVersionId != null)
            .Select(s => new { s.Id, TotalYears = s.CnpnVersion!.TotalYears })
            .ToListAsync(ct);

        return stamped.ToDictionary(s => s.Id, s => s.TotalYears);
    }

    /// <summary>
    /// Students of this promotion holding at least one stage of <em>this</em> registration that is not
    /// validated. Informational only: the jury deliberates on the whole year and PGSH sees the stages.
    /// </summary>
    private async Task<HashSet<Guid>> StudentsWithUnvalidatedStagesAsync(
        List<Registration> registrations, CancellationToken ct)
    {
        var registrationIds = registrations.Select(r => r.Id).ToList();

        var ids = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => registrationIds.Contains(a.RegistrationId))
            .Where(a => a.Result != StageAssignmentResult.Validé)
            .Select(a => a.Registration.StudentId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    private static DeliberationReport Summarize(
        string yearLabel,
        string levelLabel,
        IReadOnlyList<DeliberationRowReport> rows,
        int notCovered)
    {
        int record = rows.Count(r => r.Status == DeliberationRowStatus.WillRecord);
        int replace = rows.Count(r => r.Status == DeliberationRowStatus.WillReplace);
        int errors = rows.Count(r => r.Status.IsError());

        var counts = rows
            .Where(r => !r.Status.IsError() && r.Outcome is not null)
            .GroupBy(r => r.Outcome!.Value.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return new DeliberationReport(
            yearLabel, levelLabel, rows.Count, record, replace, errors,
            ContradictionCount: rows.Count(r => r.HasUnvalidatedStages),
            NotCovered: notCovered,
            // All-or-nothing: one bad row refuses the whole file. A promotion half closed is
            // unreconcilable — nobody can tell afterwards which verdicts made it in.
            CanApply: errors == 0 && rows.Count > 0,
            counts,
            rows);
    }

    /// <summary>The vocabulary a Moroccan faculty actually writes in a PV de déliberation, in every
    /// spelling it gets written in. Accents and casing are folded away before this is reached.</summary>
    private static RegistrationStatus? ParseDecision(string decision) => decision switch
    {
        "admis" or "admise" or "valide" or "validee" or "passe" or "a" or "ad"
            => RegistrationStatus.Validated,

        "redoublant" or "redoublante" or "redouble" or "non admis" or "non admise" or "ajourne"
            or "ajournee" or "r" or "nadm"
            => RegistrationStatus.Failed,

        "exclu" or "exclue" or "exclusion" or "e"
            => RegistrationStatus.Excluded,

        "diplome" or "diplomee" or "laureat" or "laureate" or "d"
            => RegistrationStatus.Graduated,

        "abandon" or "demission" or "desistement" or "abandonne"
            => RegistrationStatus.Withdrawn,

        _ => null,
    };

    /// <summary>An outcome a motif can qualify — the ones that go against the student.</summary>
    private static bool IsAdverse(RegistrationStatus outcome) =>
        outcome is RegistrationStatus.Failed or RegistrationStatus.Excluded or RegistrationStatus.Withdrawn;

    /// <summary>An outcome that asserts the year was cleared — the ones an unvalidated stage sits oddly with.</summary>
    private static bool IsFavourable(RegistrationStatus outcome) =>
        outcome is RegistrationStatus.Validated or RegistrationStatus.Graduated;

    // The report echoes the identifiers as the user typed them. Showing the normalized form instead
    // (lower-cased, unaccented) reads like the import mangled the file.
    private static Resolution Fail(
        DeliberationRow row, string? name, DeliberationRowStatus status, string message,
        Guid? registrationId = null) =>
        new(new DeliberationRowReport(
                row.SheetRow, Trim(row.Cne), Trim(row.Appogee), name, status, null, message, false),
            null, registrationId);

    private static Dictionary<string, List<Registration>> Index(
        List<Registration> registrations, Func<Registration, string?> key)
    {
        var index = new Dictionary<string, List<Registration>>();
        foreach (var registration in registrations)
        {
            string? k = Normalize(key(registration));
            if (k is null) continue;
            if (!index.TryGetValue(k, out var bucket)) index[k] = bucket = [];
            bucket.Add(registration);
        }
        return index;
    }

    private static string StudentName(Registration registration) =>
        $"{registration.Student?.FirstName ?? ""} {registration.Student?.LastName ?? ""}".Trim();

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Lower-cased, trimmed and stripped of accents so "Diplômé" and "diplome" are one word —
    /// the same case-insensitive discipline the search handlers and the evaluation import follow.</summary>
    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var stripped = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        return stripped.Normalize(NormalizationForm.FormC);
    }
}

internal sealed record DeliberationPlan(
    DeliberationReport Report,
    IReadOnlyList<PlannedOutcome> Work);

/// <summary>One year to close, with the registration already tracked so the entity can do it.</summary>
internal sealed record PlannedOutcome(
    Registration Registration,
    RegistrationStatus Outcome,
    string? Motif);
