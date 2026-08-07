using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Employees.MyServices;

namespace PGSH.API.Endpoints.ServicePeriods;

/// <summary>
/// The objectives of the stage a period belongs to — what the evaluation form is built from.
/// Role-neutral on purpose: the chef of the service and an administrative user both evaluate
/// through it, and <c>ExecutionAuthorizer</c> already decides which of them may act on the period.
/// </summary>
public sealed class GetPeriodObjectives : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("service-periods/{periodId:guid}/objectives", async (
            Guid periodId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetPeriodObjectivesQuery(periodId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.ServicePeriods)
        .RequireAuthorization();
    }
}
