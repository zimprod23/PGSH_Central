using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.ServicePeriods.Complete;

namespace PGSH.API.Endpoints.ServicePeriods;

public sealed class CompleteServicePeriod : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("service-periods/{id:guid}/complete", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new CompleteServicePeriodCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.ServicePeriods)
        .RequireAuthorization();
    }
}
