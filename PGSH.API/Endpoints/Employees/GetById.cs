using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Employees.GetById;

namespace PGSH.API.Endpoints.Employees;

public sealed class GetEmployeeById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("employees/{id:guid}", async (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetEmployeeByIdQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Employees)
        .RequireAuthorization();
    }
}
