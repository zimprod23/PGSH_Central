using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.BulkCreate;

namespace PGSH.API.Endpoints.Cohorts;

public sealed class BulkCreateFromPartitions : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cohorts/from-partitions",
            async (BulkCreateCohortsFromPartitionsCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.Cohorts)
            .RequireAuthorization();
    }
}
