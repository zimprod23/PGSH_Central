using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.GroupChange;

namespace PGSH.API.Endpoints.AcademicGroups;

/// <summary>
/// « Changement de groupe » — a correction, not a transfer. See
/// <see cref="ChangeStudentGroupCommand"/> for why the two are separate routes.
/// </summary>
public sealed class ChangeStudentGroup : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("groups/change-student-group", async (
            ChangeStudentGroupCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
