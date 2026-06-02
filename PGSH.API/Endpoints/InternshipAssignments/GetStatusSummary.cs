using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.InternshipAssignments.GetStatusSummary;

namespace PGSH.API.Endpoints.InternshipAssignments;

public sealed class GetAssignmentStatusSummaryEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internship-assignments/status-summary", async (
            int[]? cohortIds,
            int?   stageId,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new GetAssignmentStatusSummaryQuery(
                cohortIds?.Length > 0 ? cohortIds.ToList() : null,
                stageId);

            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.InternshipAssignments)
        .RequireAuthorization();
    }
}
