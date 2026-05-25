using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.AssignRotationGroups;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class AssignRotationGroupsEndpoint : IEndpoint
{
    public sealed record Request(int PartitionCount);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("groups/assign-partitions",
            async (int academicYearId, Request request, ISender sender, CancellationToken ct) =>
            {
                var command = new AssignRotationGroupsCommand(academicYearId, request.PartitionCount);
                var result  = await sender.Send(command, ct);
                return result.Match(count => Results.Ok(new { labeled = count }), CustomResults.Problem);
            })
            .WithTags(Tags.Groups)
            .RequireAuthorization();
    }
}
