using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.InternshipAssignments.Sheet;
using PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Stages;

/// <summary>
/// Planning a promotion from a spreadsheet. Three routes, in the order they are meant to be used:
/// download the canevas pre-filled, upload it for a dry run, upload it again to apply.
///
/// <para>The scope is one promotion — <c>levelId</c> — in one year, <c>academicYearId</c> omitted
/// resolving to the current one. <c>stageId</c> narrows the <i>download</i> to a single stage, for the
/// day the job is to fix one rotation rather than plan a year; the upload reads whatever the file
/// contains, since a canvas cut for one stage is still a canvas.</para>
///
/// <para>⚠ <b>The upload is parsed here and both routes re-parse it.</b> This is the only layer that
/// knows what a file is, and what gets applied has to be what the user uploaded — never a client
/// round-trip of rows a browser could have edited between the aperçu and the apply. The only things
/// that travel between the two calls are the two numbers the operator was shown.</para>
/// </summary>
public sealed class AffectationSheet : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("affectations/sheet/template", async (
            int? levelId,
            int? stageId,
            int? academicYearId,
            ISender sender,
            CancellationToken ct) =>
        {
            if (levelId is not { } promotion)
                return CustomResults.Problem(
                    Result.Failure<int>(AffectationSheetErrors.PromotionRequired));

            var result = await sender.Send(
                new GetAffectationSheetTemplateQuery(promotion, stageId, academicYearId), ct);

            return result.Match(
                file => Results.File(
                    file.Content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    file.FileName),
                CustomResults.Problem);
        })
        .WithName("GetAffectationSheetTemplate")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapPost("affectations/sheet/preview", async (
            IFormFile file,
            int? levelId,
            int? academicYearId,
            IAffectationSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            if (levelId is not { } promotion)
                return CustomResults.Problem(
                    Result.Failure<int>(AffectationSheetErrors.PromotionRequired));

            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(
                new PreviewAffectationSheetQuery(rows.Value, promotion, academicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("PreviewAffectationSheet")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        // confirmedCount and confirmedDroppedPeriods are what the aperçu showed. Sent back rather than
        // re-derived, so an affectation created — or a période evaluated — between the two calls
        // refuses instead of being written over by a confirmation nobody gave for it.
        app.MapPost("affectations/sheet", async (
            IFormFile file,
            int? levelId,
            int? confirmedCount,
            int? confirmedDroppedPeriods,
            int? academicYearId,
            IAffectationSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            if (levelId is not { } promotion)
                return CustomResults.Problem(
                    Result.Failure<int>(AffectationSheetErrors.PromotionRequired));

            if (confirmedCount is not { } confirmed || confirmedDroppedPeriods is not { } dropped)
                return CustomResults.Problem(
                    Result.Failure<int>(AffectationSheetErrors.ConfirmationRequired));

            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(new ApplyAffectationSheetCommand(
                rows.Value, promotion, confirmed, dropped, academicYearId, file.FileName), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("ApplyAffectationSheet")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        // ─── Walking one back ─────────────────────────────────────────────────
        //
        // Three routes again, and the same shape: list what was applied, preview the undo, apply it
        // with the number the preview showed. The undo is a first-class act, not a button on the
        // import — it has its own refusals, its own report and its own entry in the register.

        app.MapGet("affectations/imports", async (
            [AsParameters] GetAffectationImportsQuery query,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetAffectationImports")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapGet("affectations/imports/{id:guid}/reversal", async (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new PreviewAffectationImportReversalQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("PreviewAffectationImportReversal")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        // confirmedCount is what the preview showed as « affectations supprimées » — the half nothing
        // puts back. Sent back rather than re-derived, so an affectation deleted or evaluated between
        // the two calls refuses instead of being acted on under a confirmation nobody gave for it.
        app.MapPost("affectations/imports/{id:guid}/reversal", async (
            Guid id, int? confirmedCount, ISender sender, CancellationToken ct) =>
        {
            if (confirmedCount is not { } confirmed)
                return CustomResults.Problem(
                    Result.Failure<int>(AffectationImportReversalErrors.ConfirmationRequired));

            var result = await sender.Send(new ReverseAffectationImportCommand(id, confirmed), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("ReverseAffectationImport")
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }

    /// <summary>
    /// A workbook we cannot open is a bad request, not a 500 — the user picked the wrong file, and the
    /// only useful answer is to say so.
    /// </summary>
    private static Result<IReadOnlyList<AffectationSheetRow>> ReadRows(
        IFormFile file, IAffectationSheetParser parser)
    {
        try
        {
            using var stream = file.OpenReadStream();
            return Result.Success(parser.Parse(stream));
        }
        catch (Exception)
        {
            return Result.Failure<IReadOnlyList<AffectationSheetRow>>(
                AffectationSheetErrors.SheetUnreadable);
        }
    }
}
