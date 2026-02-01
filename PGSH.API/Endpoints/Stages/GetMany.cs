using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.GetMany;

namespace PGSH.API.Endpoints.Stages;

public sealed class GetMany : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("stages", async ([AsParameters] GetStagesQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages);
        //.WithName("GetStages");
    }
}
