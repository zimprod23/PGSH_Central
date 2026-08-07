using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.GetById;

namespace PGSH.API.Endpoints.Stages;

public sealed class GetById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("stages/{id:int}", async (int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetStageByIdQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
