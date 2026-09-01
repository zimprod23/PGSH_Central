using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Revalidation;

namespace PGSH.API.Endpoints.Stages;

/// <summary>
/// What re-opening this stage for this registration would mean, before anything is written.
///
/// <para>The read behind the revalidation dialog. It answers three questions the operator could not
/// otherwise get without SQL: whether the act is even permitted (decided by the very rules the
/// command applies, through <c>RevalidationPlanner</c>), which text governs this registration and
/// what duration <em>that text</em> states for the stage, and where the student failed it.</para>
/// </summary>
public sealed class GetRevalidationContext : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("registrations/{registrationId:guid}/revalidation-context", async (
            Guid registrationId, int stageId, DateOnly? from, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new GetRevalidationContextQuery(registrationId, stageId, from), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
