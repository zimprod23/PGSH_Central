using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services.Create;
using PGSH.Domain.Hospitals;

namespace PGSH.API.Endpoints.Services;

public sealed class Create : IEndpoint
{
    public sealed record Request(
        int HospitalId,
        string Name,
        int ServiceType,
        int Capacity,
        string Description);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("services", async (Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateServiceCommand(
                request.HospitalId,
                request.Name,
                (ServiceType)request.ServiceType,
                request.Capacity,
                request.Description
            );

            var result = await sender.Send(command, ct);

            return result.Match(
                id => Results.Created($"/services/{id}", id),
                CustomResults.Problem);
        })
        .WithTags(Tags.Services);
    }
}