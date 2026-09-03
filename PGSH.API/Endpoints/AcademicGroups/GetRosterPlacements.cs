using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.Placements;

namespace PGSH.API.Endpoints.AcademicGroups;

/// <summary>
/// « Quel groupe va déjà là où cet étudiant doit aller ? » — the read that makes the cheapest answer
/// to a placement request reachable. See <see cref="GetRosterPlacementsQuery"/> for why the expensive
/// answers were being taken instead.
/// </summary>
public sealed class GetRosterPlacements : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("groups/placements", async (
            [AsParameters] GetRosterPlacementsQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetRosterPlacements")
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
