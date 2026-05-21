using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.GetById;

namespace PGSH.API.Endpoints.Students;

public sealed class GetCurrent : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("students/me", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCurrentStudentQuery(), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Students)
        .RequireAuthorization();
    }
}
