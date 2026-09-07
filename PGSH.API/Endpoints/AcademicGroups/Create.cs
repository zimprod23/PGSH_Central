using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.Create;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class CreateAcademicGroup : IEndpoint
{
    public sealed record Request(
        string Label,
        int AcademicYearId,
        int? LevelId,
        string? GeographicZone,
        string? RotationGroup,
        string? Purpose);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("groups", async (Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateGroupCommand(
                request.Label,
                request.AcademicYearId,
                request.LevelId,
                request.GeographicZone,
                request.RotationGroup,
                request.Purpose);

            var result = await sender.Send(command, ct);
            return result.Match(id => Results.Created($"groups/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
