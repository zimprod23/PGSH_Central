using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.DeleteAll;

namespace PGSH.API.Endpoints.Stages;

public sealed class DeleteAllCohortsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("stages/{stageId:int}/cohorts/all",
            async (int stageId, int? academicYearId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new DeleteAllCohortsCommand(stageId, academicYearId), ct);
                return result.Match(count => Results.Ok(new { deleted = count }), CustomResults.Problem);
            })
            .WithTags(Tags.Stages)
            .RequireAuthorization();
    }
}
