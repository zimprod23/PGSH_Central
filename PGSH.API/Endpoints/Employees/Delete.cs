using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Employees.Delete;

namespace PGSH.API.Endpoints.Employees;

public sealed class DeleteEmployee : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("employees/{id:guid}", async (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteEmployeeCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Employees)
        .RequireAuthorization();
    }
}
