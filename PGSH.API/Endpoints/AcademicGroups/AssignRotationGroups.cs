using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.AssignRotationGroups;
using PGSH.Application.Stages.Planning;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class AssignRotationGroupsEndpoint : IEndpoint
{
    /// <summary>
    /// <paramref name="Strategy"/> and <paramref name="Reassign"/> both default to the historical
    /// behaviour, so an existing caller that sends only <c>partitionCount</c> is unaffected.
    /// </summary>
    public sealed record Request(
        int PartitionCount,
        PartitionStrategy Strategy = PartitionStrategy.Interleaved,
        bool Reassign = false);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // levelId is required: a partition divides one promotion, and year-wide this reached every
        // promotion of the year plus « Non réparti ». See AssignRotationGroupsCommand.
        app.MapPost("groups/assign-partitions",
            async (int academicYearId, int levelId, Request request, ISender sender, CancellationToken ct) =>
            {
                var command = new AssignRotationGroupsCommand(
                    academicYearId, request.PartitionCount, levelId, request.Strategy, request.Reassign);

                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("AssignRotationGroups")
            .WithTags(Tags.Groups)
            .RequireAuthorization();
    }
}
