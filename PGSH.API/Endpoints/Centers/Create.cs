using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Centers.Create;
using PGSH.Domain.Hospitals;

namespace PGSH.API.Endpoints.Centers;

public sealed class Create : IEndpoint
{
    public sealed record Request(
        string Name,
        int CenterType,
        string? City,
        string? LocalizationX,
        string? LocalizationY,
        string? LocalizationZ);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("centers", async (Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateCenterCommand(
                request.Name,
                (CenterType)request.CenterType,
                request.City,
                request.LocalizationX,
                request.LocalizationY,
                request.LocalizationZ
            );

            var result = await sender.Send(command, ct);

            return result.Match(
                id => Results.Created($"/centers/{id}", id),
                CustomResults.Problem);
        })
        .WithTags(Tags.Centers);
    }
}