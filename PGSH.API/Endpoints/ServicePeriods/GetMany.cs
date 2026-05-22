using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.ServicePeriods.GetMany;

namespace PGSH.API.Endpoints.ServicePeriods;

public sealed class GetServicePeriods : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("service-periods", async (
            [AsParameters] GetServicePeriodsQuery query,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.ServicePeriods)
        .RequireAuthorization();
    }
}
