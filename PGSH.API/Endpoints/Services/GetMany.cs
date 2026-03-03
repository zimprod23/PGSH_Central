using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services.GetMany;

namespace PGSH.API.Endpoints.Services;

public sealed class GetMany : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("services", async ([AsParameters] GetServicesQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Services);
    }
}