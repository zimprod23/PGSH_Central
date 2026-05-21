using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Create;

namespace PGSH.API.Endpoints.Students;

public sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("students", async (CreateStudentCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(id => Results.Created($"/students/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Students);
    }
}
