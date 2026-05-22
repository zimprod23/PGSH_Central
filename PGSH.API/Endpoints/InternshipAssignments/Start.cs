using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.InternshipAssignments.Start;

namespace PGSH.API.Endpoints.InternshipAssignments;

public sealed class StartAssignment : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("internship-assignments/{id:guid}/start", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new StartAssignmentCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.InternshipAssignments)
        .RequireAuthorization();
    }
}
