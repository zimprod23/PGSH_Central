using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.GetMany;

namespace PGSH.API.Endpoints.Hospitals;

public sealed class GetMany : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("hospitals", async ([AsParameters] GetHospitalsQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags("Hospitals");
    }
}