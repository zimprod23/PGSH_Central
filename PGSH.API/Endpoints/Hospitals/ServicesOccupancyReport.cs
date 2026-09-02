using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services.OccupancyReport;

namespace PGSH.API.Endpoints.Hospitals;

/// <summary>
/// The cross-service half of the occupancy reads — one document covering every service at once,
/// where <c>services/{id}/occupancy</c> covers one.
/// </summary>
/// <remarks>
/// Kept separate from the per-service route rather than made a mode of it: the two answer different
/// questions, carry different shapes (this one drops the occupant detail so it stays bounded across
/// 148 services), and are read by different screens.
/// </remarks>
public sealed class ServicesOccupancyReport : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("services/occupancy-report", async (
            [AsParameters] GetOccupancyReportQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Hospital)
        .RequireAuthorization();
    }
}
