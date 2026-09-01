using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.Inscription;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Registrations;

/// <summary>
/// Inscribing the students the réinscription cannot reach — the September intake, transfers from
/// another faculty, returners and réorientations. Three routes, in the order they are meant to be
/// used: download a canvas, upload it for a dry run, upload it again to apply.
///
/// <para>The scope is <b>one promotion</b>: <c>levelId</c> is required, because nobody on this sheet
/// holds a registration the level could be read from. <c>academicYearId</c> omitted resolves to the
/// current year.</para>
///
/// <para>The upload is parsed here — this is the only layer that knows what a file is — and the
/// parsed rows are what travel into the application layer. Preview and apply both re-parse the file
/// rather than trusting a client round-trip of the rows: what gets applied is what the user uploaded.</para>
/// </summary>
public sealed class Inscription : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("inscription/template", async (
            int levelId,
            int? academicYearId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetInscriptionTemplateQuery(levelId, academicYearId), ct);

            return result.Match(
                file => Results.File(
                    file.Content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    file.FileName),
                CustomResults.Problem);
        })
        .WithName("GetInscriptionTemplate")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        app.MapPost("inscription/preview", async (
            IFormFile file,
            int levelId,
            int? academicYearId,
            IInscriptionSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(
                new PreviewInscriptionQuery(rows.Value, levelId, academicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("PreviewInscription")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        // confirmedStudentCount is what the preview showed as « étudiants à créer ». Sent back rather
        // than re-derived, so a file edited between the two calls refuses instead of creating people
        // nobody was shown.
        app.MapPost("inscription", async (
            IFormFile file,
            int levelId,
            int? academicYearId,
            int? confirmedStudentCount,
            IInscriptionSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(new ApplyInscriptionCommand(
                rows.Value, levelId, academicYearId, confirmedStudentCount), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("ApplyInscription")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        // The single-row way in. No file, no preview, no count to confirm — the request is the row.
        // Every bulk import needs one of these, or fixing one person means re-sending the promotion.
        app.MapPost("inscription/student", async (
            InscribeStudentCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("InscribeStudent")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();
    }

    /// <summary>
    /// A workbook we cannot open is a bad request, not a 500 — the user picked the wrong file, and the
    /// only useful answer is to say so.
    /// </summary>
    private static Result<IReadOnlyList<InscriptionRow>> ReadRows(
        IFormFile file, IInscriptionSheetParser parser)
    {
        try
        {
            using var stream = file.OpenReadStream();
            return Result.Success(parser.Parse(stream));
        }
        catch (Exception)
        {
            return Result.Failure<IReadOnlyList<InscriptionRow>>(InscriptionErrors.SheetUnreadable);
        }
    }
}
