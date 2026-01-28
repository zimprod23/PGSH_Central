using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.GetHistory;

namespace PGSH.API.Endpoints.Students;

public sealed class GetHistory : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Path: PGSH.API.Endpoints.Students.GetHistory
        app.MapGet("students/{id:guid}/history", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new GetStudentHistoryQuery(id);
            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetStudentHistory")
        .WithTags(Tags.Students);
    }
}
