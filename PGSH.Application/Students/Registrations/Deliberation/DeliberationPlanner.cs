using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Common.Utils;
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
/// One sheet is one <b>academic year</b>, optionally narrowed to one level. The year is what makes an
/// identifier mean something — a student holds one registration per year — so a year-wide file is no
/// more ambiguous than a per-promotion one, and it is how a PV actually arrives. See
/// <see cref="DeliberationScope"/>.
/// </summary>
internal sealed class DeliberationPlanner(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
{
    /// <summary>
    /// A year-wide file is whatever the user uploaded, and the reply is a single object — exactly the
    /// shape that hides an unbounded collection. The counts stay exact; only the row list is cut.
    /// </summary>
    public const int MaxReportedRows = 1000;

    public async Task<Result<DeliberationPlan>> PlanAsync(
        DeliberationScope scope,
        IReadOnlyList<DeliberationRow> rows,
        CancellationToken ct)
    {
        var year = await yearResolver.ResolveWithLabelAsync(scope.AcademicYearId, ct);
        if (year.IsFailure)
            return Result.Failure<DeliberationPlan>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        string scopeLabel = "Toutes les promotions";
        if (scope.LevelId is { } levelId)
        {
            var level = await dbContext.Levels
                .AsNoTracking()
                .Where(l => l.Id == levelId)
                .Select(l => new { l.Label, l.Year, l.AcademicProgram })
                .FirstOrDefaultAsync(ct);

            if (level is null)
                return Result.Failure<DeliberationPlan>(RegistrationErrors.MissingLevel);

            scopeLabel = level.Label ?? $"Année {level.Year} — {level.AcademicProgram}";
        }

        // The scope itself: one registration per student. Tracked, because the apply writes through
        // RecordYearOutcome on these very rows. Level comes with them — year-wide, each row's own
        // level decides whether « Diplômé » is the last year, and the two texts of one programme
        // disagree about which year that is.
        var registrations = await dbContext.Registrations
            .Include(r => r.Student)
            .Include(r => r.Level)
            .Where(r => r.AcademicYearId == yearId)
            .Where(r => scope.LevelId == null || r.LevelId == scope.LevelId)
            .ToListAsync(ct);

        if (registrations.Count == 0)
            return Result.Failure<DeliberationPlan>(
                DeliberationErrors.PromotionHasNoStudents(scopeLabel, yearLabel));

        var finalYears = await FinalYearByStudentAsync(yearId, scope.LevelId, ct);
        var earliestFinalYear = await EarliestFinalYearByProgramAsync(registrations, ct);
        var unvalidated = await StudentsWithUnvalidatedStagesAsync(yearId, scope.LevelId, ct);

        var byCne = Index(registrations, r => r.Student?.CNE);
        var byAppogee = Index(registrations, r => r.Student?.Appogee);

        var reports = new List<DeliberationRowReport>(rows.Count);
        var work = new List<PlannedOutcome>();
        var seen = new HashSet<Guid>();

        foreach (var row in rows)
        {
            var resolved = Resolve(row, byCne, byAppogee, seen, finalYears, unvalidated);
            reports.Add(resolved.Report);
            if (resolved.Work is { } planned) work.Add(planned);
            if (resolved.RegistrationId is { } id) seen.Add(id);
        }

        var defaults = ApplyDefaults(scope, registrations, seen, finalYears, earliestFinalYear, work);

        var report = Summarize(yearLabel, scopeLabel, scope.DefaultUnlistedToAdmis, reports, defaults);
        return new DeliberationPlan(report, work);
    }

    // ---------------------------------------------------------------------------------------------
    // The rows the file actually names
    // ---------------------------------------------------------------------------------------------

    private sealed record Resolution(
        DeliberationRowReport Report,
        PlannedOutcome? Work,
        Guid? RegistrationId);

    private static Resolution Resolve(
        DeliberationRow row,
        IReadOnlyDictionary<string, List<Registration>> byCne,
        IReadOnlyDictionary<string, List<Registration>> byAppogee,
        IReadOnlySet<Guid> seen,
        IReadOnlyDictionary<Guid, int> finalYears,
        IReadOnlySet<Guid> unvalidated)
    {
        string? cne = Normalize(row.Cne);
        string? appogee = Normalize(row.Appogee);

        if (cne is null && appogee is null)
            return Fail(row, null, null, DeliberationRowStatus.NoIdentifier,
                "Ni CNE ni numéro Apogée — la ligne ne désigne aucun étudiant.");

        var byCneMatch = cne is not null ? byCne.GetValueOrDefault(cne) : null;
        var byAppogeeMatch = appogee is not null ? byAppogee.GetValueOrDefault(appogee) : null;

        // Both identifiers given and pointing at different people: one of the two cells is mistyped,
        // and picking either would close the wrong student's year.
        if (byCneMatch is not null && byAppogeeMatch is not null
            && !byCneMatch.Select(r => r.Id).ToHashSet().SetEquals(byAppogeeMatch.Select(r => r.Id)))
            return Fail(row, null, null, DeliberationRowStatus.InvalidDecision,
                "Le CNE et le numéro Apogée de cette ligne désignent deux étudiants différents.");

        var matches = byCneMatch ?? byAppogeeMatch;
        if (matches is null)
            return Fail(row, null, null, DeliberationRowStatus.UnknownStudent,
                "Aucun étudiant inscrit cette année ne porte cet identifiant.");

        // A student holds at most one registration per year (unique index), so this cannot normally
        // happen — but two students sharing an identifier can, and it is exactly the case where
        // guessing writes a verdict onto the wrong file.
        if (matches.Count > 1)
            return Fail(row, StudentName(matches[0]), LevelLabel(matches[0]),
                DeliberationRowStatus.NotInPromotion,
                "Cet identifiant est porté par plusieurs inscriptions — à traiter individuellement.");

        var registration = matches[0];
        string name = StudentName(registration);
        string levelLabel = LevelLabel(registration);

        if (seen.Contains(registration.Id))
            return Fail(row, name, levelLabel, DeliberationRowStatus.DuplicateStudent,
                "Cet étudiant apparaît déjà plus haut dans le fichier.", registration.Id);

        string? decision = Normalize(row.Decision);
        if (decision is null)
            return Fail(row, name, levelLabel, DeliberationRowStatus.MissingDecision,
                "Colonne Décision vide.", registration.Id);

        if (ParseDecision(decision) is not { } outcome)
            return Fail(row, name, levelLabel, DeliberationRowStatus.InvalidDecision,
                $"Décision « {row.Decision} » non reconnue — attendu Admis, Redoublant, Exclu, Diplômé ou Abandon.",
                registration.Id);

        // « Diplômé » is the end of a course of study, so it has to be the end of one. Where the
        // student carries no CNPN stamp the check stands aside rather than refusing: ~2,200 stamps are
        // inferred and 19 students have none at all, and blocking a whole promotion on that would make
        // the feature unusable. Same standing-aside rule as CohortProvisioner's.
        int levelYear = registration.Level?.Year ?? 0;
        if (outcome == RegistrationStatus.Graduated
            && finalYears.TryGetValue(registration.StudentId, out int totalYears)
            && levelYear != totalYears)
            return Fail(row, name, levelLabel, DeliberationRowStatus.NotAFinalYear,
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
                row.SheetRow, Trim(row.Cne), Trim(row.Appogee), name, levelLabel,
                replaces ? DeliberationRowStatus.WillReplace : DeliberationRowStatus.WillRecord,
                outcome, message,
                HasUnvalidatedStages: IsFavourable(outcome) && unvalidated.Contains(registration.StudentId)),
            new PlannedOutcome(registration, outcome, motifDropped ? null : motif),
            registration.Id);
    }

    // ---------------------------------------------------------------------------------------------
    // The students the file does not name
    // ---------------------------------------------------------------------------------------------

    private sealed record DefaultOutcomes(
        IReadOnlyList<DeliberationLevelBreakdown> ByLevel,
        int NotCovered,
        int Defaulted,
        int FinalYearUndecided,
        int AlreadyDecided,
        int NotAPromotion);

    /// <summary>
    /// Reads silence as a verdict, for the students no row of the file mentions.
    /// </summary>
    /// <remarks>
    /// <para>Three of them are deliberately left alone rather than promoted:</para>
    /// <list type="bullet">
    /// <item><b>Anyone already carrying a verdict.</b> The default never overwrites a decision someone
    /// recorded — not even an inferred one. Otherwise re-uploading last week's exceptions file, after
    /// twelve verdicts were corrected by hand, silently flips all twelve back to admis. Changing a
    /// recorded verdict is an explicit act: name the student in the file, or record it one at a time.
    /// It is also what makes this import safely re-runnable, the way the réinscription is.</item>
    /// <item><b>Anything that is not a year of study</b> — « Retrait » is a status the legacy base wore
    /// as a level (<see cref="Level.IsPromotion"/>), and there is no year to clear.</item>
    /// <item><b>Anyone in a year that may be his last.</b> ⚠ <b>The default promotes; it never
    /// graduates.</b> Measured on the real base 2026-08-18: <b>855 of the 1 657</b> students in 7ᵉ année
    /// Médecine had been in the 7ᵉ année before — 132 of them four times — and 74 of 356 in 6ᵉ année
    /// Pharmacie. The final year is the thesis year: students sit in it until they defend, and PGSH
    /// holds no record of a defence, so "still there" and "finished" are <em>both</em> ordinary and
    /// neither is derivable. Reading silence as diplômé would have graduated ~930 people who are simply
    /// still enrolled. They are counted (<c>FinalYearUndecided</c>) and left untouched; the faculty
    /// names its graduates, the defence roll being the document it actually has.</item>
    /// </list>
    /// <para>An exceptions file only works where the exception is the rare case. In a final year that
    /// is reversed, which is why the rule inverts there rather than being tuned.</para>
    /// </remarks>
    private static DefaultOutcomes ApplyDefaults(
        DeliberationScope scope,
        List<Registration> registrations,
        IReadOnlySet<Guid> seen,
        IReadOnlyDictionary<Guid, int> finalYears,
        IReadOnlyDictionary<AcademicProgram, int> earliestFinalYear,
        List<PlannedOutcome> work)
    {
        var perLevel = new Dictionary<int, LevelTally>();
        int notCovered = 0, defaulted = 0, finalYearUndecided = 0, alreadyDecided = 0, notAPromotion = 0;

        foreach (var registration in registrations)
        {
            var level = registration.Level;
            int levelId = registration.LevelId;

            if (!perLevel.TryGetValue(levelId, out var tally))
                perLevel[levelId] = tally = new LevelTally(
                    levelId, level?.Label ?? $"Niveau {levelId}", level?.Year ?? 0);

            tally.Registrations++;

            if (seen.Contains(registration.Id))
            {
                tally.Listed++;
                continue;
            }

            notCovered++;

            if (!scope.DefaultUnlistedToAdmis)
                continue;

            if (registration.Status.IsYearOutcome())
            {
                tally.AlreadyDecided++;
                alreadyDecided++;
                continue;
            }

            if (level is null || !level.IsPromotion)
            {
                notAPromotion++;
                continue;
            }

            // Might this be his last year? Then nothing is derivable and nothing is written.
            if (MayBeAFinalYear(registration, level, finalYears, earliestFinalYear))
            {
                tally.FinalYearUndecided++;
                finalYearUndecided++;
                continue;
            }

            work.Add(new PlannedOutcome(registration, RegistrationStatus.Validated, null));
            defaulted++;
            tally.WillPromote++;
        }

        var byLevel = perLevel.Values
            .OrderBy(t => t.Year)
            .ThenBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
            .Select(t => new DeliberationLevelBreakdown(
                t.LevelId, t.Label, t.Registrations, t.Listed, t.WillPromote, t.FinalYearUndecided,
                t.AlreadyDecided))
            .ToList();

        return new DefaultOutcomes(
            byLevel, notCovered, defaulted, finalYearUndecided, alreadyDecided, notAPromotion);
    }

    /// <summary>
    /// Whether this year could be the student's last — by his <em>own</em> text where he carries one,
    /// and by the shortest text of his programme where he does not.
    /// </summary>
    /// <remarks>
    /// From 2026-2027 a 6ᵉ année Médecine holds both students whose text ends there (1650.25, six years)
    /// and students who go on to a 7ᵉ (2174.18, seven), so the level alone never answers this — which is
    /// why it is asked per student. Below every text's final year the answer is the same whichever text
    /// applies, so an unstamped student needs no stamp to be safely promoted.
    /// </remarks>
    private static bool MayBeAFinalYear(
        Registration registration,
        Level level,
        IReadOnlyDictionary<Guid, int> finalYears,
        IReadOnlyDictionary<AcademicProgram, int> earliestFinalYear) =>
        finalYears.TryGetValue(registration.StudentId, out int totalYears)
            ? level.Year >= totalYears
            : earliestFinalYear.TryGetValue(level.AcademicProgram, out int earliest)
              && level.Year >= earliest;

    private sealed class LevelTally(int levelId, string label, int year)
    {
        public int LevelId { get; } = levelId;
        public string Label { get; } = label;
        public int Year { get; } = year;
        public int Registrations { get; set; }
        public int Listed { get; set; }
        public int WillPromote { get; set; }
        public int FinalYearUndecided { get; set; }
        public int AlreadyDecided { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // Lookups
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// How many years the student's own CNPN lasts, for the students of this scope that carry a
    /// stamp. Absent from the dictionary means "no text on record" — see the standing-aside rule above.
    /// </summary>
    /// <remarks>
    /// ⚠ Scoped by the <em>same predicate</em> that selected the registrations, never by shipping their
    /// ids back down. Measured 2026-08-18 on the real base: a year-wide run is 8,077 registrations, so
    /// <c>ids.Contains(…)</c> sends 8,077 parameters per lookup and the preview took over thirty
    /// seconds. Expressed as a join it is milliseconds, and it cannot drift from the scope above.
    /// </remarks>
    private async Task<Dictionary<Guid, int>> FinalYearByStudentAsync(
        int yearId, int? levelId, CancellationToken ct)
    {
        // ⚠ The registration's own text first. « Est-ce sa dernière année ? » is a question about the
        // year being deliberated, so it has to be answered by the text that governed it — and once an
        // effectivity rule can move a student mid-cursus, that is no longer the same number as the one
        // on his current stamp. The student's stamp remains the fallback for the six imported years
        // and the ~2,200 students the backfill could not reach.
        var stamped = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == yearId)
            .Where(r => levelId == null || r.LevelId == levelId)
            .Where(r => r.CnpnVersionId != null || r.Student.CnpnVersionId != null)
            .Select(r => new
            {
                r.StudentId,
                TotalYears = r.CnpnVersionId != null
                    ? r.CnpnVersion!.TotalYears
                    : r.Student.CnpnVersion!.TotalYears,
            })
            .ToListAsync(ct);

        var map = new Dictionary<Guid, int>(stamped.Count);
        foreach (var s in stamped) map[s.StudentId] = s.TotalYears;
        return map;
    }

    /// <summary>
    /// The shortest text on record per programme — the first year at which « Admis » and « Diplômé »
    /// stop being interchangeable for a student nobody has stamped.
    /// </summary>
    private async Task<Dictionary<AcademicProgram, int>> EarliestFinalYearByProgramAsync(
        List<Registration> registrations, CancellationToken ct)
    {
        var programs = registrations
            .Select(r => r.Level?.AcademicProgram)
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .Distinct()
            .ToList();

        var versions = await dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => programs.Contains(v.AcademicProgram))
            .Select(v => new { v.AcademicProgram, v.TotalYears })
            .ToListAsync(ct);

        return versions
            .GroupBy(v => v.AcademicProgram)
            .ToDictionary(g => g.Key, g => g.Min(v => v.TotalYears));
    }

    /// <summary>
    /// Students of this scope holding at least one stage of <em>this</em> registration that is not
    /// validated. Informational only: the jury deliberates on the whole year and PGSH sees the stages.
    /// </summary>
    private async Task<HashSet<Guid>> StudentsWithUnvalidatedStagesAsync(
        int yearId, int? levelId, CancellationToken ct)
    {
        var ids = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Registration.AcademicYearId == yearId)
            .Where(a => levelId == null || a.Registration.LevelId == levelId)
            .Where(a => a.Result != StageAssignmentResult.Validé)
            .Select(a => a.Registration.StudentId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    // ---------------------------------------------------------------------------------------------

    private static DeliberationReport Summarize(
        string yearLabel,
        string scopeLabel,
        bool defaultsApplied,
        IReadOnlyList<DeliberationRowReport> rows,
        DefaultOutcomes defaults)
    {
        int record = rows.Count(r => r.Status == DeliberationRowStatus.WillRecord);
        int replace = rows.Count(r => r.Status == DeliberationRowStatus.WillReplace);
        int errors = rows.Count(r => r.Status.IsError());

        var counts = rows
            .Where(r => !r.Status.IsError() && r.Outcome is not null)
            .GroupBy(r => r.Outcome!.Value.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        // The default only ever writes « Admis », so it adds to exactly one bucket.
        if (defaults.Defaulted > 0)
            counts[nameof(RegistrationStatus.Validated)] =
                counts.GetValueOrDefault(nameof(RegistrationStatus.Validated)) + defaults.Defaulted;

        return new DeliberationReport(
            yearLabel, scopeLabel, defaultsApplied,
            TotalRows: rows.Count,
            WillRecord: record,
            WillReplace: replace,
            ErrorCount: errors,
            ContradictionCount: rows.Count(r => r.HasUnvalidatedStages),
            NotCovered: defaults.NotCovered,
            DefaultedCount: defaults.Defaulted,
            FinalYearUndecidedCount: defaults.FinalYearUndecided,
            AlreadyDecidedCount: defaults.AlreadyDecided,
            NotAPromotionCount: defaults.NotAPromotion,
            // All-or-nothing: one bad row refuses the whole file. A promotion half closed is
            // unreconcilable — nobody can tell afterwards which verdicts made it in.
            CanApply: errors == 0 && rows.Count > 0,
            counts,
            defaults.ByLevel,
            rows.Take(MaxReportedRows).ToList(),
            RowsTruncated: rows.Count > MaxReportedRows);
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
        DeliberationRow row, string? name, string? levelLabel, DeliberationRowStatus status, string message,
        Guid? registrationId = null) =>
        new(new DeliberationRowReport(
                row.SheetRow, Trim(row.Cne), Trim(row.Appogee), name, levelLabel, status, null, message, false),
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

    private static string LevelLabel(Registration registration) =>
        registration.Level?.Label ?? $"Niveau {registration.LevelId}";

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
