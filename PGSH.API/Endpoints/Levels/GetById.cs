using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Levels.GetById;

namespace PGSH.API.Endpoints.Levels;

public sealed class GetLevelById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("levels/{id:int}", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetLevelByIdQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Levels)
        .RequireAuthorization();
    }
}
