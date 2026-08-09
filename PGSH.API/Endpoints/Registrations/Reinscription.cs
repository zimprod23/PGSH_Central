using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.Reinscription;

namespace PGSH.API.Endpoints.Registrations;

/// <summary>
/// The September half of the year rollover: reads a promotion's closed verdicts and creates the next
/// year's registrations. Separate from the déliberation on purpose — the two acts are months apart,
/// and not every admis actually comes back.
/// </summary>
public sealed class Reinscription : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("levels/{levelId:int}/reinscription/preview", async (
            int levelId,
            int fromAcademicYearId,
            int toAcademicYearId,
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

        app.MapPost("levels/{levelId:int}/reinscription", async (
            int levelId,
            Request request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new ApplyReinscriptionCommand(
                request.FromAcademicYearId, request.ToAcademicYearId, levelId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("ApplyReinscription")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();
    }

    public sealed record Request(int FromAcademicYearId, int ToAcademicYearId);
}
