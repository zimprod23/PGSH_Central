using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.DeleteAll;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class DeleteAllGroupsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("groups/all",
            async (int academicYearId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new DeleteAllGroupsCommand(academicYearId), ct);
                return result.Match(count => Results.Ok(new { deleted = count }), CustomResults.Problem);
            })
            .WithTags(Tags.Groups)
            .RequireAuthorization();
    }
}
