using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.GetById;

namespace PGSH.API.Endpoints.Hospitals;

public sealed class GetById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("hospitals/{id:int}", async (int id, ISender sender, CancellationToken ct) =>
        {
            var query = new GetHospitalByIdQuery(id);

            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Hospital);
    }
}