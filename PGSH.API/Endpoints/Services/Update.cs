using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services.Update;
using PGSH.Domain.Hospitals;

namespace PGSH.API.Endpoints.Services;

public sealed class Update : IEndpoint
{
    public sealed record Request(
        string Name,
        string Description,
        int ServiceType,
        int Capacity,
        int HospitalId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("services/{id:int}", async (int id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateServiceCommand(
                id,
                request.Name,
                request.Description,
                (ServiceType)request.ServiceType,
                request.Capacity,
                request.HospitalId
            );

            var result = await sender.Send(command, ct);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Services);
    }
}