using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Levels.GetMany;

namespace PGSH.API.Endpoints.Levels;

public sealed class GetMany : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("levels", async ([AsParameters] GetLevelsQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags("Levels")
        .WithName("GetLevels");
    }
}