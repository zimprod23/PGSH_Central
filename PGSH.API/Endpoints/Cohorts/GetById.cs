using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.GetById;

namespace PGSH.API.Endpoints.Cohorts;

public sealed class GetById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cohorts/{id:int}", async (int id, ISender sender, CancellationToken ct) =>
        {
            var query = new GetCohortByIdQuery(id);
            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts);
    }
}