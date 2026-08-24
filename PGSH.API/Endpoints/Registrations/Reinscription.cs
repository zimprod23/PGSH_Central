using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.Reinscription;

namespace PGSH.API.Endpoints.Registrations;

/// <summary>
/// The September half of the year rollover: reads the closed verdicts of the year that is ending and
/// creates the next year's registrations. Separate from the déliberation on purpose — the two acts are
/// months apart, and not every admis actually comes back.
///
/// <para>Year-scoped, with <c>levelId</c> narrowing it to one promotion. Omitting it rolls every
/// promotion of the closing year, each student moving up from his own level.</para>
/// </summary>
public sealed class Reinscription : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("reinscription/preview", async (
            int fromAcademicYearId,
            int toAcademicYearId,
            int? levelId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new PreviewReinscriptionQuery(fromAcademicYearId, toAcademicYearId, levelId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("PreviewReinscription")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        app.MapPost("reinscription", async (
            Request request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new ApplyReinscriptionCommand(
                request.FromAcademicYearId, request.ToAcademicYearId, request.LevelId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("ApplyReinscription")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();
    }

    public sealed record Request(int FromAcademicYearId, int ToAcademicYearId, int? LevelId);
}
