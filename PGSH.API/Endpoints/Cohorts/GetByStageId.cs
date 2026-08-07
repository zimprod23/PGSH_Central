using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.GetByStage;

namespace PGSH.API.Endpoints.Cohorts;

public sealed class GetByStageId : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("stages/{stageId:int}/cohorts", async (int stageId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCohortsByStageQuery(stageId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts)
        .RequireAuthorization();
    }
}
