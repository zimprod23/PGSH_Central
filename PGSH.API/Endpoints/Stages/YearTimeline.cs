using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Timeline;

namespace PGSH.API.Endpoints.Stages;

internal sealed class YearTimelineEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("academic-years/{academicYearId:int}/timeline",
            async (int academicYearId, int? levelId, int? stageId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetYearTimelineQuery(academicYearId, levelId, stageId), ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();
    }
}
