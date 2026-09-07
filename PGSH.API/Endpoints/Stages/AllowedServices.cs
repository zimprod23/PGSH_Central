using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.AllowedServices;
using PGSH.Domain.Stages;

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

/// <summary>
/// Authors the order the stage's services are walked in when a rotation is arranged. A PUT of the
/// whole list, not a move: the order is the resource, and a partial list is refused rather than
/// completed — see <see cref="SetAllowedServiceOrderCommand"/>.
/// </summary>
public sealed class SetAllowedServiceOrder : IEndpoint
{
    public sealed record Request(IReadOnlyList<int> ServiceIds);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("stages/{id:int}/allowed-services/order", async (
            int id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new SetAllowedServiceOrderCommand(id, request.ServiceIds ?? []);
            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}

/// <summary>
/// Declares how one authorised service is filled: by the rotation, or held for named rosters and
/// filled only by a pinned cell.
/// </summary>
/// <remarks>
/// A PUT on the authorisation itself — the mode is a property of the (stage, service) pair, not an
/// act performed on it. It rewrites nothing already placed; the next arrange is what reads it.
/// </remarks>
public sealed class SetAllowedServicePlacementMode : IEndpoint
{
    public sealed record Request(ServicePlacementMode PlacementMode);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("stages/{id:int}/allowed-services/{serviceId:int}/placement-mode", async (
            int id, int serviceId, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new SetAllowedServicePlacementModeCommand(id, serviceId, request.PlacementMode);
            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
