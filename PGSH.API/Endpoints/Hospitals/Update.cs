using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Update;
using PGSH.Domain.Hospitals;

namespace PGSH.API.Endpoints.Hospitals;

public sealed class Update : IEndpoint
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
        app.MapPut("hospitals/{id:int}", async (int id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateHospitalCommand(
                id,
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

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Hospital);
    }
}