using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Evaluations.Import;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.ServiceEvaluations;

/// <summary>
/// Bulk entry of a stage's marks from a spreadsheet. Three routes, in the order they are meant to be
/// used: download a pre-filled template, upload it for a dry run, upload it again to apply.
///
/// The upload is parsed here — this is the only layer that knows what a file is — and the parsed
/// rows are what travel into the application layer. Preview and apply both re-parse the file rather
/// than trusting a client round-trip of the rows: what gets applied is what the user uploaded.
/// </summary>
public sealed class ImportEvaluations : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // ⚠ The template route bound `scope` and `mode` as bare enums while its two siblings below
        // already went through ImportOptions + Required(). An enum is a value type, so an omitted one
        // threw inside routing before any validator ran — the download simply failed with a bare 400,
        // on the one route of the three a user reaches *first*. Same options record, same refusal.
        app.MapGet("stages/{stageId:int}/evaluations/import/template", async (
            int stageId,
            [AsParameters] ImportOptions options,
            ISender sender,
            CancellationToken ct) =>
        {
            var stated = Required(options);
            if (stated.IsFailure)
                return CustomResults.Problem(stated);

            var result = await sender.Send(new GetEvaluationImportTemplateQuery(
                stageId, stated.Value.Scope, options.PeriodNumber, stated.Value.Mode,
                options.AcademicYearId), ct);

            return result.Match(
                file => Results.File(
                    file.Content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    file.FileName),
                CustomResults.Problem);
        })
        .WithTags(Tags.ServiceEvaluations)
        .RequireAuthorization();

        app.MapPost("stages/{stageId:int}/evaluations/import/preview", async (
            int stageId,
            IFormFile file,
            [AsParameters] ImportOptions options,
            IEvaluationSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var stated = Required(options);
            if (stated.IsFailure)
                return CustomResults.Problem(stated);

            var result = await sender.Send(new PreviewEvaluationImportQuery(
                stageId, stated.Value.Scope, options.PeriodNumber, stated.Value.Mode, rows.Value,
                options.AcademicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithTags(Tags.ServiceEvaluations)
        .RequireAuthorization();

        app.MapPost("stages/{stageId:int}/evaluations/import", async (
            int stageId,
            IFormFile file,
            [AsParameters] ImportOptions options,
            IEvaluationSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var stated = Required(options);
            if (stated.IsFailure)
                return CustomResults.Problem(stated);

            var result = await sender.Send(new ImportEvaluationsCommand(
                stageId, stated.Value.Scope, options.PeriodNumber, stated.Value.Mode, rows.Value,
                options.AcademicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithTags(Tags.ServiceEvaluations)
        .RequireAuthorization();
    }

    /// <remarks>
    /// ⚠ <b>Nullable, and refused in the route rather than by the model binder</b> — see
    /// <c>OccupantsRequest</c>. An enum is a value type too, so an omitted <c>scope</c> threw inside
    /// routing before any validator ran.
    /// </remarks>
    public sealed record ImportOptions(
        EvaluationImportScope? Scope, EvaluationMode? Mode, int? PeriodNumber, int? AcademicYearId);

    /// <summary>The two the caller must state, or the refusal that says which is missing.</summary>
    private static Result<(EvaluationImportScope Scope, EvaluationMode Mode)> Required(
        ImportOptions options) =>
        options.Scope is not { } scope
            ? Result.Failure<(EvaluationImportScope, EvaluationMode)>(EvaluationImportErrors.ScopeRequired)
            : options.Mode is not { } mode
                ? Result.Failure<(EvaluationImportScope, EvaluationMode)>(EvaluationImportErrors.ModeRequired)
                : Result.Success((scope, mode));

    /// <summary>
    /// A workbook we cannot open is a bad request, not a 500 — the user picked the wrong file, and
    /// the only useful answer is to say so.
    /// </summary>
    private static Result<IReadOnlyList<EvaluationImportRow>> ReadRows(
        IFormFile file, IEvaluationSheetParser parser)
    {
        try
        {
            using var stream = file.OpenReadStream();
            return Result.Success(parser.Parse(stream));
        }
        catch (Exception)
        {
            return Result.Failure<IReadOnlyList<EvaluationImportRow>>(StageErrors.ImportSheetUnreadable);
        }
    }
}
