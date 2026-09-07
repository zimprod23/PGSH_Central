using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.Update;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class UpdateGroup : IEndpoint
{
    public sealed record Request(
        string Label, string? GeographicZone, string? RotationGroup, string? Purpose);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("groups/{id:int}", async (
            int id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateGroupCommand(
                id, request.Label, request.GeographicZone, request.RotationGroup, request.Purpose);
            var result  = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
