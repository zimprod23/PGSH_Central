using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.AssignRotationGroups;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class ClearRotationGroupsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("groups/partitions",
            async (int academicYearId, int? levelId, ISender sender, CancellationToken ct) =>
            {
                var command = new ClearRotationGroupsCommand(academicYearId, levelId);

                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("ClearRotationGroups")
            .WithTags(Tags.Groups)
            .RequireAuthorization();
    }
}
