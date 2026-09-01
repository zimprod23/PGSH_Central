using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.Delete;

namespace PGSH.API.Endpoints.Cohorts;

public sealed class DeleteCohort : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("cohorts/{id:int}", async (
            int id,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteCohortCommand(id), ct);

            // 200 with the counts, not 204: what a delete took away is exactly what the caller needs
            // to be told, and a no-content body can say nothing.
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts)
        .RequireAuthorization();
    }
}
