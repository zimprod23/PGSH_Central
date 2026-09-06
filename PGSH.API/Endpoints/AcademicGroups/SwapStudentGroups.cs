using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.GroupChange;

namespace PGSH.API.Endpoints.AcademicGroups;

/// <summary>
/// « Échanger deux étudiants » — two changements de groupe in one act, so the two rosters keep their
/// sizes.
/// </summary>
public sealed class SwapStudentGroups : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("groups/swap-students", async (
            SwapStudentGroupsCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
