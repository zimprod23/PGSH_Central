using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Delocalization;

namespace PGSH.API.Endpoints.Stages;

/// <summary>
/// Walks one délocalisation back. Its own endpoint rather than a third route on the bulk one: it is
/// the inverse of <see cref="DelocalizeStudent"/> and acts on a single student, which is what the
/// file layout in this folder says.
/// </summary>
public sealed class CancelDelocalization : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("stages/delocalize/cancel", async (
            CancelDelocalizationCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
