using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.Create;

namespace PGSH.API.Endpoints.Cohorts;

public sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cohorts", async (CreateCohortCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);

            return result.Match(
                id => Results.Created($"/api/cohorts/{id}", id),
                CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts);
    }
}