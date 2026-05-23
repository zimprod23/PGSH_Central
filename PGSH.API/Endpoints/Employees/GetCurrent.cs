using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Employees.GetById;

namespace PGSH.API.Endpoints.Employees;

public sealed class GetCurrentEmployee : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("employees/me", async (
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCurrentEmployeeQuery(), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Employees)
        .RequireAuthorization();
    }
}
