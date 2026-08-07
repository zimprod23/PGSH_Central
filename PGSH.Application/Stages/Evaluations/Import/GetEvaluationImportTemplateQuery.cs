using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.Import;

/// <summary>
/// The blank sheet, pre-filled with the stage's own students. Handing out an empty template would
/// mean everyone retypes identifiers by hand, and a mistyped CNE is a row that belongs to nobody.
/// </summary>
public sealed record GetEvaluationImportTemplateQuery(
    int StageId,
    EvaluationImportScope Scope,
    int? PeriodNumber,
    EvaluationMode Mode) : IQuery<EvaluationImportTemplateFile>;

public sealed record EvaluationImportTemplateFile(string FileName, byte[] Content);

internal sealed class GetEvaluationImportTemplateQueryHandler(
    IApplicationDbContext dbContext,
    IEvaluationSheetParser sheetParser)
    : IQueryHandler<GetEvaluationImportTemplateQuery, EvaluationImportTemplateFile>
{
    public async Task<Result<EvaluationImportTemplateFile>> Handle(
        GetEvaluationImportTemplateQuery request, CancellationToken cancellationToken)
    {
        string? stageName = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == request.StageId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (stageName is null)
            return Result.Failure<EvaluationImportTemplateFile>(StageErrors.NotFound(request.StageId));

        if (request.Scope == EvaluationImportScope.SinglePeriod)
        {
            bool periodExists = await dbContext.StageSlots
                .AnyAsync(s => s.StageId == request.StageId && s.PeriodNumber == request.PeriodNumber,
                    cancellationToken);

            if (!periodExists)
                return Result.Failure<EvaluationImportTemplateFile>(
                    StageErrors.ImportPeriodNotInStage(request.PeriodNumber ?? 0, request.StageId));
        }

        var students = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Cohort.StageId == request.StageId)
            .OrderBy(a => a.Cohort.AcademicGroup!.Label)
            .ThenBy(a => a.Registration.Student.LastName)
            .ThenBy(a => a.Registration.Student.FirstName)
            .Select(a => new EvaluationImportTemplateStudent(
                a.Registration.Student.CNE,
                a.Registration.Student.Appogee,
                ((a.Registration.Student.FirstName ?? "") + " " + (a.Registration.Student.LastName ?? "")).Trim(),
                a.Cohort.AcademicGroup!.Label ?? a.Cohort.Label))
            .ToListAsync(cancellationToken);

        var template = new EvaluationImportTemplate(
            stageName, request.Scope, request.Mode, request.PeriodNumber, students);

        string suffix = request.Scope == EvaluationImportScope.SinglePeriod
            ? $"-P{request.PeriodNumber}"
            : "-stage";

        return new EvaluationImportTemplateFile(
            $"notes-{Slug(stageName)}{suffix}.xlsx",
            sheetParser.BuildTemplate(template));
    }

    private static string Slug(string value) =>
        new(value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());
}
