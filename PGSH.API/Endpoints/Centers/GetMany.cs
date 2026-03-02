using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Centers.GetMany;

namespace PGSH.API.Endpoints.Centers;

public sealed class GetMany : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("centers", async ([AsParameters] GetCentersQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags("Centers");
    }
}
