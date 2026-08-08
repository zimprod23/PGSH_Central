using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.GetByStage;

namespace PGSH.API.Endpoints.Cohorts;

public sealed class GetByStageId : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // [AsParameters] binds StageId from the route, the year filter and paging from the query string.
        app.MapGet("stages/{stageId:int}/cohorts", async (
            [AsParameters] GetCohortsByStageQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts)
        .RequireAuthorization();
    }
}
