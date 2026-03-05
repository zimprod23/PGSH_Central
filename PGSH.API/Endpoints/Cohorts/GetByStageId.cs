using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.GetByStage;

namespace PGSH.API.Endpoints.Cohorts;

public class GetByStageId : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("stages/{stageId:int}/cohorts", async (int stageId, ISender sender, CancellationToken ct) =>
        {
            var query = new GetCohortsByStageQuery(stageId);
            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts);
    }
}
