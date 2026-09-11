using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.Manage;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class ManageGroups : IEndpoint
{
    public sealed record Request(
        int LevelId,
        int AcademicYearId,
        int? GroupSize,
        // ⚠ Nullable, and the validator refuses both or neither: a cut is asked for in one unit or
        // the other, and a default here would silently size a promotion nobody sized.
        int? GroupCount);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("groups/auto-arrange", async (
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new AutoArrangeGroupsCommand(
                LevelId: request.LevelId,
                AcademicYearId: request.AcademicYearId,
                GroupSize: request.GroupSize,
                GroupCount: request.GroupCount
            );

            var result = await sender.Send(command, cancellationToken);

            // Since this returns a BulkResponse, we return 200 OK 
            // if the operation was executed.
            return result.Match(
                response => Results.Ok(response),
                CustomResults.Problem);
        })
        .WithName("AutoArrangeGroups")
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}