using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.Join;

namespace PGSH.API.Endpoints.AcademicGroups;

/// <summary>
/// Puts a student who has no group into one, and gives him the rotations that group still has ahead
/// of it. Distinct from <see cref="TransferStudent"/>, which moves a student who is already somewhere
/// and has a running rotation to carry across.
/// </summary>
public sealed class AssignStudentToGroup : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("groups/assign-student", async (
            AssignStudentToGroupCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("AssignStudentToGroup")
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
