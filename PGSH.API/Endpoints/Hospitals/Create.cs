using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Create;

namespace PGSH.API.Endpoints.Hospitals;

public sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("hospitals", async (CreateHospitalCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(id => Results.Created($"/hospitals/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Hospital);
    }
}
