using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services.Delete;

namespace PGSH.API.Endpoints.Services;

public sealed class Delete : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("services/{id:int}", async (int id, ISender sender, CancellationToken ct) =>
        {
            var command = new DeleteServiceCommand(id);

            var result = await sender.Send(command, ct);

            // Returns 204 No Content on success
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Services);
    }
}