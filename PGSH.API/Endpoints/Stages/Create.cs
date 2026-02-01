using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Create;

namespace PGSH.API.Endpoints.Stages;

public sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("stages", async (CreateStageCommand command, ISender sender, CancellationToken ct) =>
        {
            // NO MANUAL MAPPING NEEDED HERE
            var result = await sender.Send(command, ct);

            return result.Match(
                id => Results.Created($"/stages/{id}", id),
                CustomResults.Problem);
        })
    .WithTags(Tags.Stages);
    }
}
