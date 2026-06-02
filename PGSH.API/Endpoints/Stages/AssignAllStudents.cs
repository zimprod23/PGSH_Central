using MediatR;
using Microsoft.AspNetCore.Mvc;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.AssignByStage;

namespace PGSH.API.Endpoints.Stages;

public sealed class AssignAllStudentsByStage : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("stages/{id:int}/assign-students", async (
            int id,
            [FromBody] AssignAllStudentsRequest? request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new AssignAllStudentsByStageCommand(id, request?.PartitionLabels), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}

internal sealed record AssignAllStudentsRequest(IReadOnlyList<string>? PartitionLabels);
