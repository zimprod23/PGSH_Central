using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Exports;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet;

/// <summary>
/// The canevas des affectations: one line per période, pre-filled with what the promotion already has,
/// and ready to be edited and sent back.
/// </summary>
/// <remarks>
/// <para><b>Pre-filled, never blank.</b> A blank canvas means everyone retypes identifiers by hand,
/// and a mistyped code is a line that belongs to nobody — or worse, to somebody else. It is also what
/// makes the round trip readable: what comes back is a <i>diff</i> against what went out, which is why
/// an untouched file re-uploads as « rien à écrire » rather than as a promotion's worth of
/// rewrites.</para>
///
/// <para>⚠ <b>Students with no période yet still get a line</b> — service and dates blank, one line per
/// (student, stage the level requires). Listing only what is already planned would hand back a canvas
/// that can correct the past and cannot plan the future, and the whole point of this document is the
/// promotion nobody has planned yet. ⚠ A row whose dates are left blank is reported as
/// <c>MissingDates</c> and refuses the file; a stage nobody is meant to serve is deleted from the
/// sheet, which is one keystroke and unambiguous.</para>
///
/// <para>⚠ <b>Scoped to one promotion and one year, never to all of them.</b> A stage keeps a cohorte
/// per (roster, year), so an unscoped canvas would list every promotion that ever took it.
/// <paramref name="AcademicYearId"/> omitted resolves to the current year.</para>
/// </remarks>
public sealed record GetAffectationSheetTemplateQuery(
    int LevelId,
    int? StageId = null,
    int? AcademicYearId = null) : IQuery<ExportFile>;

internal sealed class GetAffectationSheetTemplateQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    IExportWorkbookWriter writer)
    : IQueryHandler<GetAffectationSheetTemplateQuery, ExportFile>
{
    public async Task<Result<ExportFile>> Handle(
        GetAffectationSheetTemplateQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<ExportFile>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        string? levelLabel = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => l.Label)
            .FirstOrDefaultAsync(cancellationToken);

        if (levelLabel is null)
            return Result.Failure<ExportFile>(AffectationSheetErrors.LevelNotFound(request.LevelId));

        var students = await StudentsQuery(dbContext, yearId, request.LevelId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (students.Count == 0)
            return Result.Failure<ExportFile>(
                AffectationSheetErrors.PromotionHasNoStudents(levelLabel, yearLabel));

        var stages = await StagesQuery(dbContext, request.LevelId, request.StageId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var registrationIds = students.Select(s => s.RegistrationId).ToList();
        var stageIds = stages.Select(s => s.StageId).ToList();

        // Flat and top-level, folded in memory — the shape the provider accepts.
        var periods = await PeriodsQuery(dbContext, registrationIds, stageIds)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var byKey = periods
            .GroupBy(p => (p.RegistrationId, p.StageId))
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.StartDate).ToList());

        var rows = new List<IReadOnlyList<ExportCell>>();

        foreach (var student in students.OrderBy(s => s.GroupLabel).ThenBy(s => s.LastName).ThenBy(s => s.FirstName))
            foreach (var stage in stages)
            {
                if (byKey.TryGetValue((student.RegistrationId, stage.StageId), out var served))
                    rows.AddRange(served.Select(p => Row(student, stage, p)));
                else
                    rows.Add(Row(student, stage, null));
            }

        var sheet = new ExportSheet(
            "Affectations",
            $"{levelLabel} — {yearLabel}"
            + (stages.Count == 1 ? $" — {stages[0].StageName}" : string.Empty),
            [
                new ExportColumn("Apogée", 14),
                new ExportColumn("CNE", 14),
                new ExportColumn("Nom", 20),
                new ExportColumn("Prénom", 18),
                new ExportColumn("Groupe", 18),
                new ExportColumn("Stage", 26),
                new ExportColumn("Service", 26),
                new ExportColumn("Hôpital", 22),
                new ExportColumn("Début", 12),
                new ExportColumn("Fin", 12),
                new ExportColumn("Motif hors faculté", 30),
            ],
            rows,
            Notes:
            [
                "Une ligne par période. Un stage servi dans trois services est trois lignes portant le "
                + "même étudiant et le même stage.",
                "Une ligne laissée entièrement en blanc — ni service, ni dates — veut dire « pas encore "
                + "planifié » : elle est comptée et ignorée. Vous pouvez donc ne remplir qu'une partie "
                + "du fichier et le renvoyer.",
                "En revanche une ligne à moitié remplie — un service sans dates, ou des dates sans "
                + "service — fait refuser le fichier entier : c'est une ligne que quelqu'un a commencée "
                + "et pas finie, et l'ignorer laisserait un étudiant non planifié sans que rien ne le dise.",
                "« Motif hors faculté » ne se remplit que pour un service externe, et un service externe "
                + "l'exige : c'est la seule trace d'un stage que la faculté n'a pas encadré.",
                "« Apogée » et « CNE » identifient l'étudiant : ne les modifiez pas. Le CNE est absent "
                + "pour une partie des étudiants, ce qui est normal.",
                "Les colonnes « Nom », « Prénom », « Groupe » et « Hôpital » sont là pour vous : seul "
                + "« Hôpital » est lu, et uniquement pour départager deux services de même nom.",
            ]);

        string fileName = ExportFileName.Build(
            "affectations", levelLabel, yearLabel, stages.Count == 1 ? stages[0].StageName : null);
        return new ExportFile(fileName, writer.Write(new ExportWorkbook(fileName, [sheet])));
    }

    private static IReadOnlyList<ExportCell> Row(
        TemplateStudent student, TemplateStage stage, TemplatePeriod? period) =>
    [
        ExportCell.Text(student.Appogee),
        ExportCell.Text(student.Cne),
        ExportCell.Text(student.LastName),
        ExportCell.Text(student.FirstName),
        ExportCell.Text(student.GroupLabel),
        ExportCell.Text(stage.StageName),
        ExportCell.Text(period?.ServiceName),
        ExportCell.Text(period?.HospitalName),
        ExportCell.Day(period?.StartDate),
        ExportCell.Day(period?.EndDate),
        ExportCell.Text(period?.Reason),
    ];

    internal static IQueryable<TemplateStudent> StudentsQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId) =>
        dbContext.Registrations
            .Where(r => r.AcademicYearId == academicYearId && r.LevelId == levelId)
            .Select(r => new TemplateStudent(
                r.Id,
                r.Student.LastName,
                r.Student.FirstName,
                r.Student.Appogee,
                r.Student.CNE,
                r.AcademicGroup!.Label));

    internal static IQueryable<TemplateStage> StagesQuery(
        IApplicationDbContext dbContext, int levelId, int? stageId) =>
        dbContext.Stages
            .Where(s => s.LevelId == levelId && (stageId == null || s.Id == stageId))
            .OrderBy(s => s.Name)
            .Select(s => new TemplateStage(s.Id, s.Name));

    internal static IQueryable<TemplatePeriod> PeriodsQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> registrationIds,
        IReadOnlyCollection<int> stageIds) =>
        dbContext.ServicePeriods
            .Where(p => registrationIds.Contains(p.InternshipAssignment.RegistrationId)
                     && stageIds.Contains(p.InternshipAssignment.Cohort.StageId))
            .Select(p => new TemplatePeriod(
                p.InternshipAssignment.RegistrationId,
                p.InternshipAssignment.Cohort.StageId,
                p.Service.Name,
                p.Service.Hospital.Name,
                p.StartDate,
                p.EndDate,
                p.Delocalization != null ? p.Delocalization.Reason : null));
}

internal sealed record TemplateStudent(
    Guid RegistrationId,
    string LastName,
    string FirstName,
    string Appogee,
    string? Cne,
    string? GroupLabel);

internal sealed record TemplateStage(int StageId, string StageName);

internal sealed record TemplatePeriod(
    Guid RegistrationId,
    int StageId,
    string ServiceName,
    string? HospitalName,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);
