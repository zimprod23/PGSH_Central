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
        app.MapGet("stages/{stageId:int}/evaluations/import/template", async (
            int stageId,
            EvaluationImportScope scope,
            EvaluationMode mode,
            int? periodNumber,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new GetEvaluationImportTemplateQuery(stageId, scope, periodNumber, mode), ct);

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

            var result = await sender.Send(new PreviewEvaluationImportQuery(
                stageId, options.Scope, options.PeriodNumber, options.Mode, rows.Value), ct);

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

            var result = await sender.Send(new ImportEvaluationsCommand(
                stageId, options.Scope, options.PeriodNumber, options.Mode, rows.Value), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithTags(Tags.ServiceEvaluations)
        .RequireAuthorization();
    }

    public sealed record ImportOptions(EvaluationImportScope Scope, EvaluationMode Mode, int? PeriodNumber);

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
