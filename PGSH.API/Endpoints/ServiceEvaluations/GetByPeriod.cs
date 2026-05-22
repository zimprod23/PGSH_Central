using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Evaluations.GetByPeriod;

namespace PGSH.API.Endpoints.ServiceEvaluations;

public sealed class GetServiceEvaluationByPeriod : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("service-periods/{periodId:guid}/evaluation", async (
            Guid periodId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetServiceEvaluationQuery(periodId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.ServiceEvaluations)
        .RequireAuthorization();
    }
}
