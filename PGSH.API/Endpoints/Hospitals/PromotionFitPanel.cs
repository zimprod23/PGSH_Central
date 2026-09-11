using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services.PromotionFit;

namespace PGSH.API.Endpoints.Hospitals;

/// <summary>
/// « Cette promotion tient-elle ? » — the capacity read that needs no plan.
/// </summary>
/// <remarks>
/// Beside <c>services/occupancy-report</c> rather than a mode of it: that one measures the pressure a
/// répartition has already created, this one the pressure one would create, and the second is only
/// useful <em>before</em> the first can say anything at all.
/// </remarks>
public sealed class PromotionFitPanel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("services/promotion-fit", async (
            [AsParameters] GetPromotionFitQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Hospital)
        .RequireAuthorization();
    }
}
