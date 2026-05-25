using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.AllowedServices;

namespace PGSH.API.Endpoints.Stages;

public sealed class AddAllowedService : IEndpoint
{
    public sealed record Request(int ServiceId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("stages/{id:int}/allowed-services", async (
            int id, Request request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AddAllowedServiceCommand(id, request.ServiceId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}

public sealed class RemoveAllowedService : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("stages/{id:int}/allowed-services/{serviceId:int}", async (
            int id, int serviceId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RemoveAllowedServiceCommand(id, serviceId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
