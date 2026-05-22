using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.Assign;

namespace PGSH.API.Endpoints.Cohorts;

public sealed class AssignStudentsToCohort : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cohorts/{id:int}/assign-students", async (
            int id,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new AssignStudentsToCohortCommand(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts)
        .RequireAuthorization();
    }
}
