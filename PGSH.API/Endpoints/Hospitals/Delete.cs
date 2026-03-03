using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Delete;

namespace PGSH.API.Endpoints.Hospitals;

public sealed class Delete : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("hospitals/{id:int}", async (int id, ISender sender, CancellationToken ct) =>
        {
            var command = new DeleteHospitalCommand(id);

            var result = await sender.Send(command, ct);

            // Returns 204 No Content on success
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Hospital);
    }
}
