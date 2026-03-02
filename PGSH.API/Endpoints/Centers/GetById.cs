using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Centers.GetById;

namespace PGSH.API.Endpoints.Centers;

public sealed class GetById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("centers/{id:int}", async (int id, ISender sender, CancellationToken ct) =>
        {
            var query = new GetCenterByIdQuery(id);

            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Centers);
    }
}