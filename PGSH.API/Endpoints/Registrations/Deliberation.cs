using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.Deliberation;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Registrations;

/// <summary>
/// Closing a promotion's academic year from the jury's PV de déliberation. Three routes, in the order
/// they are meant to be used: download a pre-filled canvas, upload it for a dry run, upload it again
/// to apply.
///
/// The upload is parsed here — this is the only layer that knows what a file is — and the parsed rows
/// are what travel into the application layer. Preview and apply both re-parse the file rather than
/// trusting a client round-trip of the rows: what gets applied is what the user uploaded.
/// </summary>
public sealed class Deliberation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("levels/{levelId:int}/deliberation/template", async (
            int levelId,
            int? academicYearId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetDeliberationTemplateQuery(levelId, academicYearId), ct);

            return result.Match(
                file => Results.File(
                    file.Content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    file.FileName),
                CustomResults.Problem);
        })
        .WithName("GetDeliberationTemplate")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        app.MapPost("levels/{levelId:int}/deliberation/preview", async (
            int levelId,
            IFormFile file,
            int? academicYearId,
            IDeliberationSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(
                new PreviewDeliberationQuery(levelId, rows.Value, academicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("PreviewDeliberation")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        app.MapPost("levels/{levelId:int}/deliberation", async (
            int levelId,
            IFormFile file,
            int? academicYearId,
            IDeliberationSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(
                new ApplyDeliberationCommand(levelId, rows.Value, academicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("ApplyDeliberation")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();
    }

    /// <summary>
    /// A workbook we cannot open is a bad request, not a 500 — the user picked the wrong file, and the
    /// only useful answer is to say so.
    /// </summary>
    private static Result<IReadOnlyList<DeliberationRow>> ReadRows(
        IFormFile file, IDeliberationSheetParser parser)
    {
        try
        {
            using var stream = file.OpenReadStream();
            return Result.Success(parser.Parse(stream));
        }
        catch (Exception)
        {
            return Result.Failure<IReadOnlyList<DeliberationRow>>(DeliberationErrors.SheetUnreadable);
        }
    }
}
