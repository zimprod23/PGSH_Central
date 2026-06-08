using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Employees.MyServices;

namespace PGSH.API.Endpoints.Employees;

public sealed class GetMyPeriodObjectives : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("employees/me/service-periods/{periodId:guid}/objectives", async (
            Guid periodId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetPeriodObjectivesQuery(periodId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Employees)
        .RequireAuthorization();
    }
}
