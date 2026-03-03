using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Create;
using PGSH.Domain.Hospitals;

namespace PGSH.API.Endpoints.Hospitals;

public sealed class Create : IEndpoint
{
    public sealed record Request(
        int CenterId,
        string Name,
        int HospitalType,
        string City,
        string? Description,
        string? Email,
        string? LocalizationX,
        string? LocalizationY,
        string? LocalizationZ);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("hospitals", async (Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateHospitalCommand(
                request.CenterId,
                request.Name,
                (HospitalType)request.HospitalType,
                request.City,
                request.Description,
                request.Email,
                request.LocalizationX,
                request.LocalizationY,
                request.LocalizationZ
            );

            var result = await sender.Send(command, ct);

            return result.Match(
                id => Results.Created($"/hospitals/{id}", id),
                CustomResults.Problem);
        })
        .WithTags(Tags.Hospital);
    }
}