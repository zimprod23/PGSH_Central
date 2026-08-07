using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.InternshipAssignments.GetMany;
using PGSH.Domain.Common.Utils;

namespace PGSH.API.Endpoints.InternshipAssignments;

public sealed class GetManyInternshipAssignments : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internship-assignments", async (
            int[]? cohortIds,
            int? stageId,
            Guid? registrationId,
            InternshipStatus? status,
            string[]? partitionLabels,
            int? periodNumber,
            string? search,
            int? pageNumber,
            int? pageSize,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new GetInternshipAssignmentsQuery(
                cohortIds?.Length > 0 ? cohortIds.ToList() : null,
                stageId,
                registrationId,
                status,
                partitionLabels?.Length > 0 ? partitionLabels.ToList() : null,
                periodNumber,
                search,
                pageNumber ?? 1,
                pageSize ?? 20);

            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.InternshipAssignments)
        .RequireAuthorization();
    }
}
