using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Exports;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Export;

/// <summary>
/// One flat query, one sheet. ⚠ No collection subquery anywhere in the projection — that is the
/// shape Npgsql refuses, and it is what killed the macro plan with the whole suite green.
/// </summary>
internal sealed class GetStudentsExportQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ExecutionAuthorizer authorizer,
    IExportWorkbookWriter writer)
    : IQueryHandler<GetStudentsExportQuery, ExportFile>
{
    /// <summary>
    /// Above this the file stops being a document and starts being an outage. The whole base holds
    /// 10 204 students, so a year-scoped export cannot reach it — the guard bites only if a caller
    /// ever finds a way to widen past a single year.
    /// </summary>
    internal const int MaxRows = 20_000;

    public async Task<Result<ExportFile>> Handle(
        GetStudentsExportQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(ExportErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<ExportFile>(access.Error);

        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<ExportFile>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        string? levelLabel = null;
        if (request.LevelId is { } requestedLevel)
        {
            var level = await dbContext.Levels
                .AsNoTracking()
                .Where(l => l.Id == requestedLevel)
                .Select(l => new { l.Label, l.Year, l.AcademicProgram })
                .FirstOrDefaultAsync(cancellationToken);

            if (level is null)
                return Result.Failure<ExportFile>(RegistrationErrors.MissingLevel);

            levelLabel = ExportLabels.Level(level.Label, level.Year, level.AcademicProgram);
        }

        var query = RegistrationsQuery(
            dbContext, yearId, request.LevelId, request.Program,
            request.AcademicGroupId, request.SearchTerm);

        int rowCount = await query.CountAsync(cancellationToken);
        if (rowCount > MaxRows)
            return Result.Failure<ExportFile>(ExportErrors.TooManyRows(
                rowCount, MaxRows, "une promotion, un groupe ou une recherche"));

        var rows = await query.ToListAsync(cancellationToken);

        string scope = levelLabel
            ?? (request.Program is { } program ? ExportLabels.Program(program) : "toutes promotions");

        var cells = rows.Select(ToCells).ToList();

        var sheet = new ExportSheet(
            "Étudiants",
            $"Étudiants — {scope} — {yearLabel} — {ExportLabels.Count(rows.Count)} inscription(s)",
            Columns,
            cells,
            await NotesAsync(cells, yearId, request.LevelId, cancellationToken));

        var workbook = new ExportWorkbook(
            ExportFileName.Build("etudiants", levelLabel ?? request.Program?.ToString(), yearLabel),
            [sheet]);

        return new ExportFile(workbook.FileName, writer.Write(workbook));
    }

    /// <summary>
    /// What the document says about its own blanks.
    ///
    /// <para>⚠ The roster note is the one worth the extra query. « Groupe » empty on every line has
    /// two causes that call for opposite acts — nobody has cut the promotion, or the cut exists and
    /// nobody has been placed in it — and a blank column collapses them into a third reading the
    /// reader reaches first: that the export is broken. Measured 2026-08-31: 2026-2027 held
    /// <b>90 rosters and 0 inscriptions rattachées</b>, and that is precisely how it was reported.</para>
    /// </summary>
    private async Task<IReadOnlyList<string>> NotesAsync(
        IReadOnlyList<IReadOnlyList<ExportCell>> cells,
        int yearId,
        int? levelId,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();

        if (ExportNotes.EmptyColumnsNote(Columns, cells) is { } empty)
            notes.Add(empty);

        // Asked only when the answer will be printed — the ordinary export pays nothing for it.
        if (ExportNotes.EmptyColumns(Columns, cells).Contains(GroupColumnHeader))
        {
            int rosters = await dbContext.AcademicGroups
                .AsNoTracking()
                .Where(g => g.AcademicYearId == yearId
                         && (levelId == null || g.LevelId == levelId))
                .CountAsync(cancellationToken);

            notes.Add(ExportNotes.RosterNote(rosters));
        }

        return notes;
    }

    /// <summary>Named once, so the note and the column cannot drift apart.</summary>
    private const string GroupColumnHeader = "Groupe";

    private static readonly IReadOnlyList<ExportColumn> Columns =
    [
        new("Nom", 22),
        new("Prénom", 20),
        new("CNE", 16),
        new("Apogée", 14),
        new("CIN", 14),
        new("Sexe", 7),
        new("Date de naissance", 16),
        new("E-mail", 30),
        new("Programme", 14),
        new("Niveau", 22),
        new("Année universitaire", 18),
        new(GroupColumnHeader, 22),
        new("N° groupe", 10),
        new("Partition", 10),
        new("Statut", 14),
        new("Source de la décision", 18),
        new("CNPN", 16),
        new("Origine CNPN", 14),
        new("Convention", 14),
    ];

    private static IReadOnlyList<ExportCell> ToCells(StudentExportRow row) =>
    [
        ExportCell.Text(row.LastName),
        ExportCell.Text(row.FirstName),
        ExportCell.Text(row.Cne),
        ExportCell.Text(row.Appogee),
        ExportCell.Text(row.Cin),
        ExportCell.Text(ExportLabels.Gender(row.Gender)),
        ExportCell.Day(row.DateOfBirth),
        ExportCell.Text(row.Email),
        ExportCell.Text(ExportLabels.Program(row.Program)),
        ExportCell.Text(ExportLabels.Level(row.LevelLabel, row.LevelYear, row.Program)),
        ExportCell.Text(row.YearLabel),
        ExportCell.Text(row.GroupLabel),
        ExportCell.Count(row.GroupNumber),
        ExportCell.Text(row.RotationGroup),
        ExportCell.Text(ExportLabels.RegistrationStatus(row.Status)),
        ExportCell.Text(ExportLabels.OutcomeSource(row.OutcomeSource)),
        ExportCell.Text(row.RegistrationCnpnCode ?? row.StudentCnpnCode),
        ExportCell.Text(CnpnOrigin(row)),
        ExportCell.Text(ExportLabels.Agreement(row.Agreement)),
    ];

    /// <summary>
    /// ⚠ The read order is <c>r.CnpnVersionId ?? r.Student.CnpnVersionId</c>, and the export says
    /// which of the two answered. Null is not « owes nothing », it is « jamais résolu » — ~2 200
    /// enrolled students carry no stamp at all — and a text read off the student rather than off the
    /// registration is precisely the one that can still move under him.
    /// </summary>
    private static string CnpnOrigin(StudentExportRow row) =>
        row.RegistrationCnpnCode is not null ? "Inscription"
        : row.StudentCnpnCode is not null ? "Étudiant"
        : "";

    /// <summary>
    /// Named, <c>internal static</c> and taking the context, so <c>SqlTranslationTests</c> can hand
    /// it to <c>ToQueryString()</c>. A query buried in a private async method cannot be compiled
    /// without running it, and the in-memory provider translates nothing.
    /// </summary>
    internal static IQueryable<StudentExportRow> RegistrationsQuery(
        IApplicationDbContext dbContext,
        int yearId,
        int? levelId,
        AcademicProgram? program,
        int? academicGroupId,
        string? searchTerm)
    {
        IQueryable<Registration> query = dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == yearId);

        if (levelId is { } level)
            query = query.Where(r => r.LevelId == level);

        if (program is { } wanted)
            query = query.Where(r => r.Level.AcademicProgram == wanted);

        if (academicGroupId is { } groupId)
            query = query.Where(r => r.AcademicGroupId == groupId);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            // Same shape as every other search handler: trimmed, lowered on *both* sides, and
            // applied to every field in the predicate — one field left un-lowered is a silent bug.
            string term = searchTerm.Trim().ToLower();
            query = query.Where(r =>
                r.Student.FirstName.ToLower().Contains(term) ||
                r.Student.LastName.ToLower().Contains(term) ||
                r.Student.CNE.ToLower().Contains(term) ||
                r.Student.Appogee.ToLower().Contains(term) ||
                (r.Student.CIN != null && r.Student.CIN.ToLower().Contains(term)));
        }

        return query
            .OrderBy(r => r.Level.AcademicProgram)
            .ThenBy(r => r.Level.Year)
            .ThenBy(r => r.AcademicGroup != null ? r.AcademicGroup.GroupNumber : int.MaxValue)
            .ThenBy(r => r.Student.LastName)
            .ThenBy(r => r.Student.FirstName)
            .Select(r => new StudentExportRow(
                r.Student.LastName,
                r.Student.FirstName,
                r.Student.CNE,
                r.Student.Appogee,
                r.Student.CIN,
                r.Student.Gender,
                r.Student.DateOfBirth,
                r.Student.Email,
                r.Level.AcademicProgram,
                r.Level.Year,
                r.Level.Label,
                r.AcademicYear.Label,
                r.AcademicGroup != null ? r.AcademicGroup.Label : null,
                r.AcademicGroup != null ? r.AcademicGroup.GroupNumber : null,
                r.AcademicGroup != null ? r.AcademicGroup.RotationGroup : null,
                r.Status,
                r.OutcomeSource,
                r.CnpnVersion != null ? r.CnpnVersion.Code : null,
                r.Student.CnpnVersion != null ? r.Student.CnpnVersion.Code : null,
                r.Student.AgreementType));
    }
}

/// <summary>One row of the roll — flat by construction, so the projection stays translatable.</summary>
internal sealed record StudentExportRow(
    string LastName,
    string FirstName,
    string Cne,
    string Appogee,
    string? Cin,
    Gender Gender,
    DateOnly? DateOfBirth,
    string Email,
    AcademicProgram Program,
    int LevelYear,
    string? LevelLabel,
    string YearLabel,
    string? GroupLabel,
    int? GroupNumber,
    string? RotationGroup,
    RegistrationStatus Status,
    RegistrationOutcomeSource? OutcomeSource,
    string? RegistrationCnpnCode,
    string? StudentCnpnCode,
    AgreementType Agreement);
