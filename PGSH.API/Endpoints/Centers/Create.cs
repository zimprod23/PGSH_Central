using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Centers.Create;

namespace PGSH.API.Endpoints.Centers;

public sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("centers", async (CreateCenterCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(id => Results.Created($"/centers/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Centers);
    }
}
