using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.Partitioning;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class GetPromotionPartitioning : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("groups/partitioning", async (
            [AsParameters] GetPromotionPartitioningQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetPromotionPartitioning")
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
