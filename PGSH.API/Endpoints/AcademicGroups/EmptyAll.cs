using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.Empty;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class EmptyAllGroupsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("groups/all/students",
            async (int academicYearId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new EmptyAllYearGroupsCommand(academicYearId), ct);
                return result.Match(count => Results.Ok(new { unassigned = count }), CustomResults.Problem);
            })
            .WithTags(Tags.Groups)
            .RequireAuthorization();
    }
}
