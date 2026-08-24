using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.Deliberation;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Registrations;

/// <summary>
/// Closing an academic year from the jury's PV de déliberation. Three routes, in the order they are
/// meant to be used: download a canvas, upload it for a dry run, upload it again to apply.
///
/// <para>The scope is the <b>year</b> — <c>academicYearId</c> omitted resolves to the current one —
/// with <c>levelId</c> narrowing it to a single promotion when that is what the jury sat on. A file
/// covering every level of one year is not ambiguous: a student holds one registration per year.</para>
///
/// <para>The upload is parsed here — this is the only layer that knows what a file is — and the parsed
/// rows are what travel into the application layer. Preview and apply both re-parse the file rather
/// than trusting a client round-trip of the rows: what gets applied is what the user uploaded.</para>
/// </summary>
public sealed class Deliberation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("deliberation/template", async (
            int? levelId,
            int? academicYearId,
            DeliberationTemplateMode? mode,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetDeliberationTemplateQuery(
                levelId, academicYearId, mode ?? DeliberationTemplateMode.Exceptions), ct);

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

        app.MapPost("deliberation/preview", async (
            IFormFile file,
            int? levelId,
            int? academicYearId,
            bool? defaultUnlistedToAdmis,
            IDeliberationSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(new PreviewDeliberationQuery(
                rows.Value, levelId, academicYearId, defaultUnlistedToAdmis ?? false), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery()
        .WithName("PreviewDeliberation")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        // confirmedDefaultCount is what the preview showed as « admis par défaut ». Sent back rather
        // than re-derived, so a registration created between the two calls refuses instead of being
        // promoted by a confirmation nobody gave for it.
        app.MapPost("deliberation", async (
            IFormFile file,
            int? levelId,
            int? academicYearId,
            bool? defaultUnlistedToAdmis,
            int? confirmedDefaultCount,
            IDeliberationSheetParser parser,
            ISender sender,
            CancellationToken ct) =>
        {
            var rows = ReadRows(file, parser);
            if (rows.IsFailure)
                return CustomResults.Problem(rows);

            var result = await sender.Send(new ApplyDeliberationCommand(
                rows.Value, levelId, academicYearId,
                defaultUnlistedToAdmis ?? false, confirmedDefaultCount), ct);

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
