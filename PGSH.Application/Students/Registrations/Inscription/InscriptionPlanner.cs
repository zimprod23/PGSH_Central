using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Progression;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Inscription;

/// <summary>
/// Turns an uploaded inscription sheet into the exact set of writes it would perform, and reports
/// every row that cannot be written and why.
///
/// <para>Preview and apply both run this and nothing else, so the dry run the user confirmed is
/// literally the plan that executes — the same guarantee the déliberation and the evaluation import
/// make, for the same reason.</para>
///
/// <para><b>This is the third act of the year, and the only one that starts from nothing.</b> The
/// déliberation reads the registrations of the closing year and writes verdicts onto them; the
/// réinscription reads those verdicts and creates the next year's registrations. Both begin from a
/// registration the student already holds, which is exactly why neither can see the people this act
/// exists for: the September intake, and anyone arriving from outside or coming back after an
/// absence. They hold no registration to be read.</para>
/// </summary>
internal sealed class InscriptionPlanner(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    FinalYearGuard finalYear)
{
    /// <summary>
    /// An intake file is a whole promotion and the reply is a single object — exactly the shape that
    /// hides an unbounded collection. The counts stay exact; only the row list is cut, and refusals
    /// are ordered first so the cap can never hide a row somebody has to act on.
    /// </summary>
    public const int MaxReportedRows = 1000;

    /// <summary>
    /// Where a manufactured address lives — and the <em>whole</em> generation rule, local part
    /// included, is <c>StudentIdentifierRules</c>'s. Two copies would give one faculty two address
    /// namespaces: measured while writing this, the copy here kept digits as well as letters, so
    /// « Mohamed2 Alaoui » would have become <c>mohamed2_alaoui</c> here and <c>mohamed_alaoui</c> in
    /// the importer, for the same person.
    /// </summary>
    public const string EmailDomain = StudentIdentifierRules.DefaultEmailDomain;

    /// <summary>
    /// How far the numeric suffix is walked before a row is refused. Bounded on purpose: the
    /// alternative to a ceiling is a loop that cannot say when it has failed.
    /// </summary>
    private const int MaxEmailAttempts = 500;

    /// <summary>
    /// Marks a CNE that was manufactured because the row carried none — never a real national code,
    /// and readable as such wherever it is displayed. Same intent as
    /// <c>LegacyIdentityMapper.SyntheticCnePrefix</c>.
    /// </summary>
    public const string ProvisionalCnePrefix = "SANS-CNE-";

    /// <summary>
    /// The same, for a numéro Apogée the faculty has not allocated yet.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Both identifiers are NOT NULL UNIQUE on <c>Students</c>, so neither may be left out and
    /// neither may be "".</b> The filtered index <c>IX_Student_Appogee</c> reads
    /// « WHERE Appogee IS NOT NULL » and looks as though absence were allowed, but the column itself
    /// is required — the filter can never be false — so an empty string is a *value* and the second
    /// student without an Apogée would collide with the first. A row is therefore required to carry
    /// one of the two, and whichever is missing is manufactured from the other, which is unique.
    /// </remarks>
    public const string ProvisionalAppogeePrefix = "SANS-APOGEE-";

    public async Task<Result<InscriptionPlan>> PlanAsync(
        InscriptionScope scope,
        IReadOnlyList<InscriptionRow> rows,
        CancellationToken ct)
    {
        var year = await yearResolver.ResolveWithLabelAsync(scope.AcademicYearId, ct);
        if (year.IsFailure)
            return Result.Failure<InscriptionPlan>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == scope.LevelId)
            .Select(l => new { l.Id, l.Label, l.Year, l.AcademicProgram })
            .FirstOrDefaultAsync(ct);

        if (level is null)
            return Result.Failure<InscriptionPlan>(RegistrationErrors.MissingLevel);

        string levelLabel = level.Label ?? $"Année {level.Year} — {level.AcademicProgram}";

        // « Retrait » is a status the legacy base wore as a level. Nobody is inscribed into one, and
        // the check belongs here rather than per row: it refuses the file, not a line of it.
        if (level.Year <= 0)
            return Result.Failure<InscriptionPlan>(InscriptionErrors.NotAPromotion(levelLabel));

        var known = await MatchKnownStudentsAsync(rows, ct);
        var registeredThisYear = await RegisteredInYearAsync(yearId, known.ByStudentId.Keys, ct);

        // Only the students PGSH already holds can owe it a stage. A newcomer has no cursus here, so
        // there is nothing for the guard to read and nothing for it to refuse.
        var returningIds = known.ByStudentId.Keys.Where(id => !registeredThisYear.Contains(id)).ToList();
        var finalYearRefusals = await finalYear.EnsureMayEnterManyAsync(
            returningIds, level.Id, yearId, ct);

        var drafts = new List<RowDraft>(rows.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            drafts.Add(Classify(
                row, level.Year, level.AcademicProgram, known, registeredThisYear, finalYearRefusals, seen));
        }

        await AllocateEmailsAsync(drafts, ct);

        var report = Summarize(yearLabel, levelLabel, drafts);
        return new InscriptionPlan(report, level.Id, yearId, level.AcademicProgram, drafts);
    }

    // -------------------------------------------------------------------------------------------
    // Classification
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Decides what one row is, in the order the questions can actually be answered: is the line
    /// usable at all, does PGSH already hold this person, is he already registered, and only then
    /// what kind of arrival it is.
    /// </summary>
    private static RowDraft Classify(
        InscriptionRow row,
        int levelYear,
        AcademicProgram levelProgram,
        KnownStudents known,
        IReadOnlySet<Guid> registeredThisYear,
        IReadOnlyDictionary<Guid, Error> finalYearRefusals,
        HashSet<string> seen)
    {
        string? cne = Normalize(row.Cne);
        string? appogee = Normalize(row.Appogee);
        string? cin = Normalize(row.Cin);
        string? email = Normalize(row.Email);

        string name = FullName(row);

        if (cne is null && appogee is null)
            return Refuse(row, name, InscriptionAction.NoIdentifier,
                "Ni CNE ni numéro Apogée — la ligne ne désigne aucun étudiant.");

        // ⚠ CNE and Apogée *identify*; CIN and e-mail only corroborate. All four are unique in the
        // store, but only the first two are what a row is understood to name — so a line whose CNE is
        // unknown while its e-mail happens to belong to somebody is a mistyped cell, not that person
        // registering. Treated as a match it would silently give an existing student a registration
        // under a newcomer's name; treated as a newcomer it would violate the index at SaveChanges
        // with nothing actionable in the message. It is neither: it is a line to look at.
        var byIdentity = Present(known.Find(known.ByCne, cne), known.Find(known.ByAppogee, appogee));

        if (byIdentity.Select(m => m.Id).Distinct().Count() > 1)
            return Refuse(row, name, InscriptionAction.IdentifierConflict,
                "Le CNE et le numéro Apogée de cette ligne désignent deux étudiants déjà "
                + "enregistrés — l'un des deux est erroné.");

        var student = byIdentity.FirstOrDefault();

        var corroborating = Present(known.Find(known.ByCin, cin), known.Find(known.ByEmail, email));
        if (corroborating.FirstOrDefault(m => student is null || m.Id != student.Id) is { } other)
            return Refuse(row, name, InscriptionAction.IdentifierConflict,
                $"Le CIN ou l'adresse de cette ligne appartient déjà à {other.FirstName} "
                + $"{other.LastName} (CNE {other.Cne}), qui n'est pas l'étudiant que le CNE désigne.");

        // ⚠ The same person twice in one file is a 500 waiting at SaveChanges, not a duplicate row:
        // IX_Registration_Student_Year is unique, and so are all four identifiers on Students. Every
        // identifier the row carries is claimed, not just the first — two lines for one new person,
        // one written with his CNE and one with his Apogée, would otherwise both pass here.
        if (Claim(seen, student, cne, appogee, cin, email))
            return Refuse(row, name, InscriptionAction.DuplicateInFile,
                "Cet étudiant, ou l'un de ses identifiants, apparaît déjà plus haut dans le fichier.");

        var origin = ReadOrigin(row);
        if (origin is null && HasAnyOrigin(row))
            return Refuse(row, name, InscriptionAction.InvalidValue,
                "Provenance incomplète : l'établissement, la dernière année suivie et la référence "
                + "d'équivalence sont requis ensemble.");

        return student is null
            ? ClassifyNewcomer(row, name, levelYear, origin)
            : ClassifyKnown(row, name, student, levelProgram, registeredThisYear, finalYearRefusals, origin);
    }

    private static List<StudentIdentity> Present(params StudentIdentity?[] matches) =>
        matches.Where(m => m is not null).Select(m => m!).ToList();

    /// <summary>
    /// Claims every identifier this row carries, and the student it resolved to. Returns true when any
    /// of them was already claimed by an earlier row.
    /// </summary>
    /// <remarks>
    /// Every claim is registered even when one of them fails, so the sets stay complete for the rows
    /// below — a second duplicate must be reported against its own line, not swallowed because the
    /// first already tripped.
    /// </remarks>
    private static bool Claim(
        HashSet<string> seen, StudentIdentity? student, params string?[] identifiers)
    {
        bool duplicate = student is not null && !seen.Add($"id:{student.Id}");

        foreach (string? identifier in identifiers)
        {
            if (identifier is null) continue;
            if (!seen.Add($"v:{identifier}")) duplicate = true;
        }

        return duplicate;
    }

    private static RowDraft ClassifyNewcomer(
        InscriptionRow row, string name, int levelYear, OriginDraft? origin)
    {
        if (string.IsNullOrWhiteSpace(row.LastName) && string.IsNullOrWhiteSpace(row.FirstName))
            return Refuse(row, name, InscriptionAction.MissingName,
                "Nom et prénom absents : un étudiant ne peut pas être créé sans identité.");

        // ⚠ Above the first year the équivalence is the whole point of the row. Without it the
        // student's dossier opens in the middle of a cursus with nothing saying the years below were
        // recognised — and the day « ce qu'il doit » is read from the CNPN's requirement set rather
        // than from his failed attempts, he owes every stage of the years he did elsewhere.
        if (levelYear > 1 && origin is null)
            return Refuse(row, name, InscriptionAction.OriginRequired,
                $"Inscription en {levelYear}ᵉ année d'un étudiant inconnu de la faculté : indiquez "
                + "l'établissement d'origine, la dernière année qui y a été suivie et la référence "
                + "de la décision d'équivalence.");

        var fields = ReadFields(row);
        if (fields.IsFailure)
            return Refuse(row, name, InscriptionAction.InvalidValue, fields.Error.Description);

        var action = levelYear > 1 ? InscriptionAction.TransferIn : InscriptionAction.NewEntrant;

        string message = action == InscriptionAction.TransferIn
            ? $"Transfert entrant : création de l'étudiant, inscription, et équivalence pour "
              + $"{origin!.LastLevelYearCompleted} année(s) à « {origin.Institution} »."
            : "Nouvel inscrit : création de l'étudiant et de son inscription.";

        // ⚠ CNE and Apogée are both NOT NULL UNIQUE, and an international student legitimately has no
        // CNE while a numéro Apogée is often allocated after the affectation list arrives — the legacy
        // import hit the first wall on 4 693 of 10 203 rows and manufactured `LEGACY-n`. Whichever is
        // missing is built from the other, which is unique and therefore cannot collide: a second row
        // carrying it would have matched the student this one creates and been classified as
        // returning rather than as a newcomer.
        string? generatedCne = null;
        if (Normalize(row.Cne) is null)
        {
            generatedCne = $"{ProvisionalCnePrefix}{Trim(row.Appogee)}";

            // ⚠ A validator describes what a *save* must satisfy. A code the CNE pattern rejects makes
            // the student read-only the day somebody opens his file, and the refusal then names a
            // field nobody was editing — exactly how 5 646 students became unsaveable once already.
            // The prefix costs 9 of the 20 characters allowed, so a long Apogée really does overflow.
            if (!StudentIdentifierRules.IsValidCne(generatedCne))
                return Refuse(row, name, InscriptionAction.InvalidValue,
                    $"CNE absent, et le code provisoire « {generatedCne} » dérivé du numéro Apogée "
                    + "ne serait pas un identifiant enregistrable : renseignez la colonne CNE.");

            message += $" CNE absent : code provisoire « {generatedCne} » attribué.";
        }

        string? generatedAppogee = null;
        if (Normalize(row.Appogee) is null)
        {
            generatedAppogee = $"{ProvisionalAppogeePrefix}{Trim(row.Cne)}";

            if (generatedAppogee.Length > StudentIdentifierRules.MaxAppogeeLength)
                return Refuse(row, name, InscriptionAction.InvalidValue,
                    "Numéro Apogée absent, et le CNE est trop long pour en dériver un : renseignez "
                    + "la colonne Apogée.");

            message += $" Numéro Apogée absent : « {generatedAppogee} » attribué en attendant.";
        }

        return new RowDraft(
            row, action, name, null, fields.Value, origin,
            NeedsEmail: Normalize(row.Email) is null, Message: message,
            GeneratedCne: generatedCne, GeneratedAppogee: generatedAppogee);
    }

    private static RowDraft ClassifyKnown(
        InscriptionRow row,
        string name,
        StudentIdentity student,
        AcademicProgram levelProgram,
        IReadOnlySet<Guid> registeredThisYear,
        IReadOnlyDictionary<Guid, Error> finalYearRefusals,
        OriginDraft? origin)
    {
        string knownName = $"{student.FirstName} {student.LastName}".Trim();
        if (knownName.Length == 0) knownName = name;

        // Idempotence, deliberately not an error: this act creates people, so the file has to survive
        // being re-sent with the late arrivals appended.
        if (registeredThisYear.Contains(student.Id))
            return new RowDraft(
                row, InscriptionAction.AlreadyRegistered, knownName, student, null, null,
                NeedsEmail: false,
                Message: "Déjà inscrit pour cette année universitaire — ligne ignorée.");

        if (finalYearRefusals.TryGetValue(student.Id, out var blocked))
            return Refuse(row, knownName, InscriptionAction.FinalYearBlocked, blocked.Description);

        bool changesProgramme = student.AcademicProgram != levelProgram;

        string message = changesProgramme
            ? $"Réorientation {student.AcademicProgram} → {levelProgram} : le programme de l'étudiant "
              + "et son rattachement CNPN suivent."
            : "Étudiant déjà connu, sans inscription cette année : nouvelle inscription.";

        if (origin is not null)
            message += $" Équivalence enregistrée pour « {origin.Institution} ».";

        return new RowDraft(
            row,
            changesProgramme ? InscriptionAction.ProgrammeChange : InscriptionAction.Returning,
            knownName, student, null, origin,
            NeedsEmail: false, Message: message);
    }

    // -------------------------------------------------------------------------------------------
    // Cells
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Everything a <see cref="Student"/> needs beyond its identifiers, with the optional cells
    /// resolved. Only the cells the file actually carries are read: <c>Status</c> keeps the entity's
    /// own default (civil, marocaine) rather than being guessed at from a column nobody filled.
    /// </summary>
    private static Result<StudentFields> ReadFields(InscriptionRow row)
    {
        var gender = ParseEnum(row.Gender, GenderWords);
        if (gender.IsFailure) return Result.Failure<StudentFields>(gender.Error);

        var bac = ParseEnum(row.BacSeries, BacSeriesWords);
        if (bac.IsFailure) return Result.Failure<StudentFields>(bac.Error);

        var agreement = ParseEnum(row.Agreement, AgreementWords);
        if (agreement.IsFailure) return Result.Failure<StudentFields>(agreement.Error);

        var birth = ParseDate(row.DateOfBirth, "Date de naissance");
        if (birth.IsFailure) return Result.Failure<StudentFields>(birth.Error);

        var grade = ParseDecimal(row.AccessGrade, "Note d'accès");
        if (grade.IsFailure) return Result.Failure<StudentFields>(grade.Error);

        return new StudentFields(
            Gender: gender.Value is { } g ? (Gender)g : Domain.Users.Gender.None,
            BacSeries: bac.Value is { } b ? (BacSeries)b : Domain.Students.BacSeries.SVT,
            Agreement: agreement.Value is { } a ? (AgreementType)a : AgreementType.None,
            DateOfBirth: birth.Value,
            PlaceOfBirth: Trim(row.PlaceOfBirth),
            BacYear: Trim(row.BacYear) ?? "",
            AccessGrade: grade.Value);
    }

    /// <summary>
    /// The équivalence, or null when the row carries none. All three of establishment, last year and
    /// reference are needed together: two of the three is a record that cannot say what it recognised.
    /// </summary>
    private static OriginDraft? ReadOrigin(InscriptionRow row)
    {
        string? institution = Trim(row.OriginInstitution);
        string? reference = Trim(row.EquivalenceReference);
        var lastYear = ParseInt(row.OriginLastYearCompleted);

        if (institution is null || reference is null || lastYear is null or <= 0)
            return null;

        var date = ParseDate(row.EquivalenceDate, "Date d'équivalence");

        return new OriginDraft(
            institution, Trim(row.OriginCountry), lastYear.Value, reference,
            date.IsSuccess ? date.Value : null);
    }

    private static bool HasAnyOrigin(InscriptionRow row) =>
        Trim(row.OriginInstitution) is not null
        || Trim(row.EquivalenceReference) is not null
        || Trim(row.OriginLastYearCompleted) is not null
        || Trim(row.OriginCountry) is not null;

    // -------------------------------------------------------------------------------------------
    // Manufactured addresses
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Hands every student-creating row without an e-mail the address <c>prenom_nom@um5.ac.ma</c>,
    /// suffixed until it is free.
    /// </summary>
    /// <remarks>
    /// <para><b>Why generate at all.</b> <c>Users.Email</c> is NOT NULL UNIQUE and an intake list from
    /// scolarité routinely has no address column — the students have not been given one yet. Refusing
    /// the file over it would make the whole act unusable in September; the legacy import faced the
    /// identical wall and manufactured all 10 204.</para>
    ///
    /// <para>⚠ <b>And why it is never silent.</b> An address is a login:
    /// <c>SyncUserMiddleware</c> links a Keycloak <c>sub</c> to a local user by
    /// <c>IdentityProviderId</c> and <b>falls back to matching on e-mail</b>. Manufacturing one that
    /// somebody already holds would hand a student another person's account. So the taken set is read
    /// from the store — not merely from the batch — and every generated address is reported on its own
    /// row and counted in <c>InscriptionReport.GeneratedEmails</c>.</para>
    ///
    /// <para>The lookup is built only when a row will actually read it, the same discipline
    /// <c>SchedulePublisher.EnsureIntakeAsync</c> follows: a file that carries its own addresses costs
    /// no query at all.</para>
    /// </remarks>
    private async Task AllocateEmailsAsync(List<RowDraft> drafts, CancellationToken ct)
    {
        var needing = drafts.Where(d => d.NeedsEmail && d.Action.Writes()).ToList();
        if (needing.Count == 0)
            return;

        var taken = new HashSet<string>(
            await TakenEmailsQuery(dbContext, EmailDomain).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        // Addresses the file supplies itself are not in the store yet and must not be handed out twice.
        foreach (var draft in drafts.Where(d => d.Action.Writes()))
        {
            if (Normalize(draft.Row.Email) is { } supplied)
                taken.Add(supplied);
        }

        for (int i = 0; i < needing.Count; i++)
        {
            var draft = needing[i];
            string local = LocalPart(draft.Row);

            string? allocated = null;
            for (int suffix = 0; suffix < MaxEmailAttempts; suffix++)
            {
                string candidate = StudentIdentifierRules.EmailCandidate(local, suffix, EmailDomain);

                if (taken.Add(candidate))
                {
                    allocated = candidate;
                    break;
                }
            }

            needing[i] = allocated is null
                ? Refuse(draft.Row, draft.StudentFullName, InscriptionAction.EmailUnavailable,
                    $"Aucune adresse libre pour « {local}@{EmailDomain} » : renseignez la colonne E-mail.")
                : draft with { GeneratedEmail = allocated };
        }

        // The drafts are replaced in place so the report and the work list stay one list.
        var replacements = needing.ToDictionary(d => d.Row.SheetRow);
        for (int i = 0; i < drafts.Count; i++)
        {
            if (replacements.TryGetValue(drafts[i].Row.SheetRow, out var replaced))
                drafts[i] = replaced;
        }
    }

    /// <summary>
    /// The address's local part, from the one rule both generators share. A name that yields nothing
    /// falls back to the row's own identifier, which is unique by the time this is reached.
    /// </summary>
    private static string LocalPart(InscriptionRow row)
    {
        string local = StudentIdentifierRules.EmailLocalPart(row.FirstName, row.LastName);
        if (local.Length > 0) return local;

        string? identifier = Slug(row.Cne) ?? Slug(row.Appogee);
        return $"etudiant{identifier ?? row.SheetRow.ToString(CultureInfo.InvariantCulture)}";
    }

    // -------------------------------------------------------------------------------------------
    // Lookups — named, because a query buried in a private async method cannot be handed to
    // ToQueryString() and the in-memory provider translates nothing. See SqlTranslationTests.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Every student the file could already be talking about, matched on any of the four identifiers
    /// that are unique in the store.
    /// </summary>
    /// <remarks>
    /// ⚠ A flat, top-level projection keyed on the student — never a collection subquery — and the
    /// comparisons are lower-cased on both sides, the same discipline every search handler follows.
    /// A single field left un-lowered here does not merely fail to find a student: it creates a
    /// second one carrying an identifier the unique index will then refuse.
    /// </remarks>
    internal static IQueryable<StudentIdentity> StudentsByIdentifierQuery(
        IApplicationDbContext dbContext,
        IReadOnlyList<string> cnes,
        IReadOnlyList<string> appogees,
        IReadOnlyList<string> cins,
        IReadOnlyList<string> emails) =>
        dbContext.Students
            .AsNoTracking()
            .Where(s => cnes.Contains(s.CNE.ToLower())
                     || (s.Appogee != null && appogees.Contains(s.Appogee.ToLower()))
                     || (s.CIN != null && cins.Contains(s.CIN.ToLower()))
                     || emails.Contains(s.Email.ToLower()))
            .Select(s => new StudentIdentity(
                s.Id, s.CNE, s.Appogee, s.CIN, s.Email, s.FirstName, s.LastName,
                s.AcademicProgram, s.CnpnVersionId));

    /// <summary>Which of these students already hold a registration in the target year.</summary>
    internal static IQueryable<Guid> RegisteredInYearQuery(
        IApplicationDbContext dbContext, int academicYearId, IReadOnlyList<Guid> studentIds) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == academicYearId && studentIds.Contains(r.StudentId))
            .Select(r => r.StudentId);

    /// <summary>
    /// Every address already in use on the generation domain. Narrowed to the domain because a
    /// manufactured address can only ever collide there, and read as one column so the whole set fits
    /// in a single round trip — this act runs a handful of times a year.
    /// </summary>
    internal static IQueryable<string> TakenEmailsQuery(IApplicationDbContext dbContext, string domain) =>
        dbContext.Users
            .AsNoTracking()
            .Where(u => u.Email.EndsWith("@" + domain))
            .Select(u => u.Email);

    private async Task<KnownStudents> MatchKnownStudentsAsync(
        IReadOnlyList<InscriptionRow> rows, CancellationToken ct)
    {
        var cnes = Distinct(rows, r => r.Cne);
        var appogees = Distinct(rows, r => r.Appogee);
        var cins = Distinct(rows, r => r.Cin);
        var emails = Distinct(rows, r => r.Email);

        if (cnes.Count == 0 && appogees.Count == 0 && cins.Count == 0 && emails.Count == 0)
            return KnownStudents.Empty;

        var found = await StudentsByIdentifierQuery(dbContext, cnes, appogees, cins, emails)
            .ToListAsync(ct);

        return KnownStudents.From(found);
    }

    private async Task<HashSet<Guid>> RegisteredInYearAsync(
        int academicYearId, IEnumerable<Guid> studentIds, CancellationToken ct)
    {
        var ids = studentIds.ToList();
        if (ids.Count == 0) return [];

        var registered = await RegisteredInYearQuery(dbContext, academicYearId, ids).ToListAsync(ct);
        return registered.ToHashSet();
    }

    private static List<string> Distinct(
        IReadOnlyList<InscriptionRow> rows, Func<InscriptionRow, string?> selector) =>
        rows.Select(selector)
            .Select(Normalize)
            .Where(v => v is not null)
            .Select(v => v!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // -------------------------------------------------------------------------------------------

    private static InscriptionReport Summarize(
        string yearLabel, string levelLabel, IReadOnlyList<RowDraft> drafts)
    {
        int Count(InscriptionAction action) => drafts.Count(d => d.Action == action);

        int errors = drafts.Count(d => d.Action.IsError());
        int creates = drafts.Count(d => d.CreatesStudent);

        var byAction = drafts
            .GroupBy(d => d.Action.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        // Refusals first, so the cap can never hide the rows somebody has to act on — the same
        // ordering rule the réinscription's report follows, for the same reason.
        var ordered = drafts
            .OrderByDescending(d => d.Action.IsError())
            .ThenBy(d => d.Row.SheetRow)
            .Select(d => new InscriptionRowReport(
                d.Row.SheetRow, Trim(d.Row.Cne), Trim(d.Row.Appogee), d.StudentFullName,
                d.Action, d.CreatesStudent, d.GeneratedEmail, d.Origin is not null, d.Message))
            .ToList();

        return new InscriptionReport(
            yearLabel,
            levelLabel,
            TotalRows: drafts.Count,
            WillCreateStudents: creates,
            WillRegister: drafts.Count(d => d.Action.Writes()),
            NewEntrants: Count(InscriptionAction.NewEntrant),
            TransfersIn: Count(InscriptionAction.TransferIn),
            Returning: Count(InscriptionAction.Returning),
            ProgrammeChanges: Count(InscriptionAction.ProgrammeChange),
            AlreadyRegistered: Count(InscriptionAction.AlreadyRegistered),
            ErrorCount: errors,
            GeneratedEmails: drafts.Count(d => d.GeneratedEmail is not null),
            OriginsRecorded: drafts.Count(d => d.Action.Writes() && d.Origin is not null),
            // All-or-nothing on errors: half an intake is unreconcilable, and the half that landed
            // created people. Rows already registered are not errors and do not block.
            CanApply: errors == 0 && drafts.Count > 0,
            byAction,
            ordered.Take(MaxReportedRows).ToList(),
            RowsTruncated: ordered.Count > MaxReportedRows);
    }

    private static RowDraft Refuse(
        InscriptionRow row, string name, InscriptionAction action, string message) =>
        new(row, action, name, null, null, null, NeedsEmail: false, Message: message);

    private static string FullName(InscriptionRow row)
    {
        string name = $"{Trim(row.FirstName) ?? ""} {Trim(row.LastName) ?? ""}".Trim();
        return name.Length == 0 ? "(sans nom)" : name;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Lower-cased, trimmed and stripped of accents — the same folding the déliberation and
    /// every search handler use, so « Diplômé » and "diplome" are one word and so are two spellings
    /// of an identifier.</summary>
    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var stripped = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        return stripped.Normalize(NormalizationForm.FormC);
    }

    private static string? Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    // -------------------------------------------------------------------------------------------
    // Cell parsing. Every one of these reports against its own row rather than throwing: a mistyped
    // date on line 300 must not cost the reading of lines 301 to 700.
    // -------------------------------------------------------------------------------------------

    private static Result<int?> ParseEnum(string? value, IReadOnlyDictionary<string, int> words)
    {
        if (Normalize(value) is not { } folded)
            return Result.Success<int?>(null);

        return words.TryGetValue(folded, out int parsed)
            ? Result.Success<int?>(parsed)
            : Result.Failure<int?>(Error.Validation(
                "Inscription.InvalidValue", $"Valeur « {value!.Trim()} » non reconnue."));
    }

    private static Result<DateOnly?> ParseDate(string? value, string columnLabel)
    {
        if (Trim(value) is not { } text)
            return Result.Success<DateOnly?>(null);

        // Culture-invariant first (what ClosedXML writes back from a real date cell), then the two
        // French spellings a human types.
        string[] formats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"];

        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? Result.Success<DateOnly?>(parsed)
            : Result.Failure<DateOnly?>(Error.Validation(
                "Inscription.InvalidValue",
                $"{columnLabel} « {text} » illisible — attendu jj/mm/aaaa."));
    }

    private static Result<decimal?> ParseDecimal(string? value, string columnLabel)
    {
        if (Trim(value) is not { } text)
            return Result.Success<decimal?>(null);

        // A French keyboard writes 14,25 and a workbook exports 14.25. Both are the same number.
        string normalized = text.Replace(',', '.');

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? Result.Success<decimal?>(parsed)
            : Result.Failure<decimal?>(Error.Validation(
                "Inscription.InvalidValue", $"{columnLabel} « {text} » n'est pas un nombre."));
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(Trim(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    private static readonly Dictionary<string, int> GenderWords = new(StringComparer.Ordinal)
    {
        ["m"] = (int)Domain.Users.Gender.Male,
        ["h"] = (int)Domain.Users.Gender.Male,
        ["homme"] = (int)Domain.Users.Gender.Male,
        ["masculin"] = (int)Domain.Users.Gender.Male,
        ["male"] = (int)Domain.Users.Gender.Male,
        ["f"] = (int)Domain.Users.Gender.Female,
        ["femme"] = (int)Domain.Users.Gender.Female,
        ["feminin"] = (int)Domain.Users.Gender.Female,
        ["female"] = (int)Domain.Users.Gender.Female,
    };

    private static readonly Dictionary<string, int> BacSeriesWords = new(StringComparer.Ordinal)
    {
        ["svt"] = (int)Domain.Students.BacSeries.SVT,
        ["sciences de la vie et de la terre"] = (int)Domain.Students.BacSeries.SVT,
        ["pc"] = (int)Domain.Students.BacSeries.Physique,
        ["physique"] = (int)Domain.Students.BacSeries.Physique,
        ["sciences physiques"] = (int)Domain.Students.BacSeries.Physique,
        ["math a"] = (int)Domain.Students.BacSeries.MathA,
        ["matha"] = (int)Domain.Students.BacSeries.MathA,
        ["sm a"] = (int)Domain.Students.BacSeries.MathA,
        ["math b"] = (int)Domain.Students.BacSeries.MathB,
        ["mathb"] = (int)Domain.Students.BacSeries.MathB,
        ["sm b"] = (int)Domain.Students.BacSeries.MathB,
        ["bac francais"] = (int)Domain.Students.BacSeries.BacFrançais,
        ["francais"] = (int)Domain.Students.BacSeries.BacFrançais,
        ["mission"] = (int)Domain.Students.BacSeries.BacMission,
        ["bac mission"] = (int)Domain.Students.BacSeries.BacMission,
        ["etranger"] = (int)Domain.Students.BacSeries.Etrangaire,
        ["etrangere"] = (int)Domain.Students.BacSeries.Etrangaire,
    };

    private static readonly Dictionary<string, int> AgreementWords = new(StringComparer.Ordinal)
    {
        ["aucune"] = (int)AgreementType.None,
        ["none"] = (int)AgreementType.None,
        ["payee amie"] = (int)AgreementType.PayeeAmie,
        ["pays amis"] = (int)AgreementType.PayeeAmie,
        ["payee-amie"] = (int)AgreementType.PayeeAmie,
        ["international"] = (int)AgreementType.International,
        ["internationale"] = (int)AgreementType.International,
        ["autre"] = (int)AgreementType.Autre,
    };
}

/// <summary>The identifiers of a student PGSH already holds, flat and projected.</summary>
internal sealed record StudentIdentity(
    Guid Id,
    string Cne,
    string? Appogee,
    string? Cin,
    string Email,
    string FirstName,
    string LastName,
    AcademicProgram AcademicProgram,
    int? CnpnVersionId);

/// <summary>The four identifier indexes, built once for the whole file.</summary>
internal sealed record KnownStudents(
    IReadOnlyDictionary<Guid, StudentIdentity> ByStudentId,
    IReadOnlyDictionary<string, StudentIdentity> ByCne,
    IReadOnlyDictionary<string, StudentIdentity> ByAppogee,
    IReadOnlyDictionary<string, StudentIdentity> ByCin,
    IReadOnlyDictionary<string, StudentIdentity> ByEmail)
{
    public static readonly KnownStudents Empty = new(
        new Dictionary<Guid, StudentIdentity>(),
        new Dictionary<string, StudentIdentity>(),
        new Dictionary<string, StudentIdentity>(),
        new Dictionary<string, StudentIdentity>(),
        new Dictionary<string, StudentIdentity>());

    public StudentIdentity? Find(IReadOnlyDictionary<string, StudentIdentity> index, string? key) =>
        key is not null && index.TryGetValue(key, out var found) ? found : null;

    public static KnownStudents From(IReadOnlyList<StudentIdentity> students)
    {
        var byId = new Dictionary<Guid, StudentIdentity>();
        var byCne = new Dictionary<string, StudentIdentity>(StringComparer.Ordinal);
        var byAppogee = new Dictionary<string, StudentIdentity>(StringComparer.Ordinal);
        var byCin = new Dictionary<string, StudentIdentity>(StringComparer.Ordinal);
        var byEmail = new Dictionary<string, StudentIdentity>(StringComparer.Ordinal);

        foreach (var student in students)
        {
            byId[student.Id] = student;
            Index(byCne, student.Cne, student);
            Index(byAppogee, student.Appogee, student);
            Index(byCin, student.Cin, student);
            Index(byEmail, student.Email, student);
        }

        return new KnownStudents(byId, byCne, byAppogee, byCin, byEmail);

        static void Index(Dictionary<string, StudentIdentity> index, string? key, StudentIdentity student)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            index[key.Trim().ToLowerInvariant()] = student;
        }
    }
}

/// <summary>The cells of a new student's record that are not identifiers.</summary>
internal sealed record StudentFields(
    Gender Gender,
    BacSeries BacSeries,
    AgreementType Agreement,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string BacYear,
    decimal? AccessGrade);

/// <summary>The équivalence a row carries, before it becomes a <see cref="PriorEnrolment"/>.</summary>
internal sealed record OriginDraft(
    string Institution,
    string? Country,
    int LastLevelYearCompleted,
    string EquivalenceReference,
    DateOnly? EquivalenceDate);

/// <summary>
/// One planned row: what it will do, and everything the apply needs to do it. The report is projected
/// from these, so the preview and the write can never describe different things.
/// </summary>
internal sealed record RowDraft(
    InscriptionRow Row,
    InscriptionAction Action,
    string StudentFullName,
    StudentIdentity? Student,
    StudentFields? Fields,
    OriginDraft? Origin,
    bool NeedsEmail,
    string Message,
    string? GeneratedCne = null,
    string? GeneratedAppogee = null,
    string? GeneratedEmail = null)
{
    public bool CreatesStudent =>
        Action is InscriptionAction.NewEntrant or InscriptionAction.TransferIn;
}

internal sealed record InscriptionPlan(
    InscriptionReport Report,
    int LevelId,
    int AcademicYearId,
    AcademicProgram Programme,
    IReadOnlyList<RowDraft> Drafts);
