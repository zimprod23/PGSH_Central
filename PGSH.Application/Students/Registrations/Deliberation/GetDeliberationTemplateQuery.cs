using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Deliberation;

/// <summary>
/// The déliberation sheet to fill in. Handing out a bare header row would mean someone retypes
/// identifiers by hand, and a mistyped CNE is a row that belongs to nobody — so both modes carry the
/// students: <see cref="DeliberationTemplateMode.Full"/> as the rows to decide,
/// <see cref="DeliberationTemplateMode.Exceptions"/> as a reference list beside an empty sheet.
///
/// Scoped to one academic year — omitted resolves to the current one, never to all of them — and
/// narrowed to one level only when <paramref name="LevelId"/> is given.
/// </summary>
public sealed record GetDeliberationTemplateQuery(
    int? LevelId = null,
    int? AcademicYearId = null,
    DeliberationTemplateMode Mode = DeliberationTemplateMode.Exceptions) : IQuery<DeliberationTemplateFile>;

public sealed record DeliberationTemplateFile(string FileName, byte[] Content);

internal sealed class GetDeliberationTemplateQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ExecutionAuthorizer authorizer,
    IDeliberationSheetParser sheetParser)
    : IQueryHandler<GetDeliberationTemplateQuery, DeliberationTemplateFile>
{
    public async Task<Result<DeliberationTemplateFile>> Handle(
        GetDeliberationTemplateQuery request, CancellationToken cancellationToken)
    {
        // The canvas is a nominative list of a whole promotion; it is scolarité's document.
        var access = authorizer.EnsureIsAdministrative(DeliberationErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<DeliberationTemplateFile>(access.Error);

        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<DeliberationTemplateFile>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        string scopeLabel = "Toutes les promotions";
        if (request.LevelId is { } levelId)
        {
            var level = await dbContext.Levels
                .AsNoTracking()
                .Where(l => l.Id == levelId)
                .Select(l => new { l.Label, l.Year, l.AcademicProgram })
                .FirstOrDefaultAsync(cancellationToken);

            if (level is null)
                return Result.Failure<DeliberationTemplateFile>(RegistrationErrors.MissingLevel);

            scopeLabel = level.Label ?? $"Année {level.Year} — {level.AcademicProgram}";
        }

        var students = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == yearId)
            .Where(r => request.LevelId == null || r.LevelId == request.LevelId)
            .OrderBy(r => r.Level.Year)
            .ThenBy(r => r.AcademicGroup!.GroupNumber)
            .ThenBy(r => r.Student.LastName)
            .ThenBy(r => r.Student.FirstName)
            .Select(r => new DeliberationTemplateStudent(
                r.Student.CNE,
                r.Student.Appogee,
                ((r.Student.FirstName ?? "") + " " + (r.Student.LastName ?? "")).Trim(),
                r.Level.Label ?? ("Année " + r.Level.Year),
                r.AcademicGroup != null ? (r.AcademicGroup.Label ?? "") : "",
                // Pre-filling a verdict already recorded is what makes a correction pass practical:
                // the jury re-downloads, changes the two lines it got wrong, and re-uploads.
                r.OutcomeSource != null ? r.Status.ToString() : null))
            .ToListAsync(cancellationToken);

        // An empty sheet is worse than an error: it looks like the promotion has no students, when in
        // fact the year picker is on a year this level did not run.
        if (students.Count == 0)
            return Result.Failure<DeliberationTemplateFile>(
                DeliberationErrors.PromotionHasNoStudents(scopeLabel, yearLabel));

        var template = new DeliberationTemplate(yearLabel, scopeLabel, request.Mode, students);

        string prefix = request.Mode == DeliberationTemplateMode.Exceptions
            ? "deliberation-exceptions"
            : "deliberation";

        return new DeliberationTemplateFile(
            $"{prefix}-{Slug(scopeLabel)}-{Slug(yearLabel)}.xlsx",
            sheetParser.BuildTemplate(template));
    }

    private static string Slug(string value) =>
        new(value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());
}
