using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Centers.Update;
using PGSH.Domain.Hospitals;

namespace PGSH.API.Endpoints.Centers;

public sealed class Update : IEndpoint
{
    public sealed record Request(
        string Name,
        CenterType CenterType,
        string? City,
        string? LocalizationX,
        string? LocalizationY,
        string? LocalizationZ);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("centers/{id:int}", async (int id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateCenterCommand(
                id, request.Name, request.CenterType, request.City,
                request.LocalizationX, request.LocalizationY, request.LocalizationZ);

            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Centers);
    }
}
