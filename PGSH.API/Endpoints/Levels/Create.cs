using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Levels.Create;

namespace PGSH.API.Endpoints.Levels;

public sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("levels", async (CreateLevelCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);
            return result.Match(id => Results.Created($"/levels/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Levels);
    }
}
