using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Delocalization;

namespace PGSH.API.Endpoints.Stages;

public sealed class DelocalizeStudent : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("stages/delocalize", async (
            DelocalizeStudentCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
