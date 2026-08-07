using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.InternshipAssignments.GetRecord;

namespace PGSH.API.Endpoints.InternshipAssignments;

public sealed class GetStudentStageRecord : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internship-assignments/{id:guid}/record", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetStudentStageRecordQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.InternshipAssignments)
        .RequireAuthorization();
    }
}
