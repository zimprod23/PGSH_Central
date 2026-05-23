using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Levels.Delete;

namespace PGSH.API.Endpoints.Levels;

public sealed class DeleteLevel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("levels/{id:int}", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteLevelCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Levels);
    }
}
