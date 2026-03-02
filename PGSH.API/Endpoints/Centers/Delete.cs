using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Centers.Delete;

namespace PGSH.API.Endpoints.Centers;

public sealed class Delete : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("centers/{id:int}", async (int id, ISender sender, CancellationToken ct) =>
        {
            var command = new DeleteCenterCommand(id);

            var result = await sender.Send(command, ct);

            // Returns 204 No Content on success (Standard for Delete)
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Centers);
    }
}
