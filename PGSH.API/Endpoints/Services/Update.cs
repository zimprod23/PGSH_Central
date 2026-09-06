using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services;
using PGSH.Application.Hospitals.Services.Update;
using PGSH.Domain.Hospitals;

namespace PGSH.API.Endpoints.Services;

public sealed class Update : IEndpoint
{
    public sealed record Request(
        string Name,
        string Description,
        ServiceType ServiceType,
        int Capacity,
        int HospitalId,
        string? Specialty,
        string? LocalizationX,
        string? LocalizationY,
        string? LocalizationZ,
        IReadOnlyCollection<ServiceLevelCapacityRequest>? LevelCapacities,
        /// <summary>
        /// ⚠ Nullable so an omission is « le client n'en dit rien » rather than « refuser le
        /// dépassement ». A non-nullable bool binds to false when the field is absent, which would
        /// make every service strict the moment an older client saved one — a restriction nobody
        /// authored, on the one flag whose whole purpose is that somebody authored it.
        /// </summary>
        bool? AllowsOverCapacity,
        /// <summary>
        /// ⚠ Nullable, and null means « unchanged ». A service hors faculté that an older client
        /// saved without this field would be pulled back into the rotation and into the saturation
        /// of every service it borders — with students already délocalisés standing on it.
        /// </summary>
        bool? IsExternal);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("services/{id:int}", async (int id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateServiceCommand(
                id, request.Name, request.Description, request.ServiceType,
                request.Capacity, request.HospitalId, request.Specialty,
                request.LocalizationX, request.LocalizationY, request.LocalizationZ,
                request.LevelCapacities,
                request.AllowsOverCapacity ?? true,
                request.IsExternal);

            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Services)
        .RequireAuthorization();
    }
}
