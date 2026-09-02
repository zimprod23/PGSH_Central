using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Endpoints.Exports;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.ReinscriptionSheet;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Registrations;

/// <summary>
/// The year rollover as the faculty actually hands it over: one spreadsheet naming, per student, the
/// étape he was in and the étape he enters. Two routes — upload for a dry run, upload again to apply.
///
/// <para><b>Why this sits beside <c>reinscription/</c> rather than replacing it.</b> That route
/// derives next year from verdicts already recorded and needs the déliberation to have run first.
/// This one is handed the answer, so it closes the year and opens the next in a single act — which is
/// the order the faculty works in when it sends a réinscription roll rather than a PV.</para>
///
/// <para><b>There is no template route.</b> The other three canvases are documents PGSH hands out and
/// gets back; this one is the faculty's own, and generating a rival version of it would only invite
/// the two to drift.</para>
///
/// <para>The upload is parsed here — this is the only layer that knows what a file is — and the
/// parsed rows are what travel into the application layer. Preview and apply both re-parse the file
/// rather than trusting a client round-trip of the rows: what gets applied is what the user uploaded.</para>
/// </summary>
public sealed class ReinscriptionSheet : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("reinscription/sheet/preview", async (
            IFormFile file,
            int fromAcademicYearId,
            int toAcademicYearId,
            IReinscriptionSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(new PreviewReinscriptionSheetQuery(
                rows.Value, fromAcademicYearId, toAcademicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("PreviewReinscriptionSheet")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        // confirmedGraduationCount is what the preview showed as « diplômés déduits de leur absence ».
        // Sent back rather than re-derived, so a registration created between the two calls refuses
        // instead of having its cursus ended by a confirmation nobody gave for it.
        app.MapPost("reinscription/sheet", async (
            IFormFile file,
            int fromAcademicYearId,
            int toAcademicYearId,
            int? confirmedGraduationCount,
            IReinscriptionSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(new ApplyReinscriptionSheetCommand(
                rows.Value, fromAcademicYearId, toAcademicYearId, confirmedGraduationCount), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("ApplyReinscriptionSheet")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        // The same upload, as the three-sheet document scolarité works from. ⚠ It writes nothing, so
        // it is deliberately usable *before* the confirmation and on a roll that would be refused —
        // « donne-moi la liste des erreurs » is the request, and an apply that names only the first
        // offending line cannot answer it. The report on screen is capped; this file is not.
        app.MapPost("reinscription/sheet/export", async (
            IFormFile file,
            int fromAcademicYearId,
            int toAcademicYearId,
            IReinscriptionSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(new GetReinscriptionSheetExportQuery(
                rows.Value, fromAcademicYearId, toAcademicYearId), ct);

            return result.Match(
                export => Results.File(export.Content, ExportContentType.Xlsx, export.FileName),
                CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("ExportReinscriptionSheetReport")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();
    }

    /// <summary>
    /// A workbook we cannot open is a bad request, not a 500 — the user picked the wrong file, and
    /// the only useful answer is to say so.
    /// </summary>
    private static Result<IReadOnlyList<ReinscriptionSheetRow>> ReadRows(
        IFormFile file, IReinscriptionSheetParser parser)
    {
        try
        {
            using var stream = file.OpenReadStream();
            return Result.Success(parser.Parse(stream));
        }
        catch (Exception)
        {
            return Result.Failure<IReadOnlyList<ReinscriptionSheetRow>>(
                ReinscriptionSheetErrors.SheetUnreadable);
        }
    }
}
