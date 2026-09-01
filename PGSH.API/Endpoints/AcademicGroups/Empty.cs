using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.Empty;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class EmptyGroup : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // dropAffectations is the caller having read the refusal that named the count — never a
        // default. Without it a roster holding affectations is refused rather than half-emptied.
        app.MapDelete("groups/{id:int}/students", async (
            int id,
            bool? dropAffectations,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new EmptyGroupCommand(id, dropAffectations ?? false), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
