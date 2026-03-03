using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services.GetById;

namespace PGSH.API.Endpoints.Services;

public sealed class GetById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("services/{id:int}", async (int id, ISender sender, CancellationToken ct) =>
        {
            var query = new GetServiceByIdQuery(id);
            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Services);
    }
}