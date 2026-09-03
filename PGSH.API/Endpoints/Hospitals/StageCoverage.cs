using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Coverage;

namespace PGSH.API.Endpoints.Hospitals;

/// <summary>
/// « Cet hôpital peut-il accueillir toute la rotation de cette promotion ? » — asked before the
/// placement is promised, not discovered at the sixth cell. See
/// <see cref="GetHospitalStageCoverageQuery"/>.
/// </summary>
public sealed class StageCoverage : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // The level is a required query parameter rather than an optional one: coverage is a fact
        // about a hospital *and* a promotion, and a hospital's coverage of nothing in particular is
        // not a question anyone asks.
        app.MapGet("hospitals/{hospitalId:int}/stage-coverage", async (
            int hospitalId, int levelId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetHospitalStageCoverageQuery(hospitalId, levelId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetHospitalStageCoverage")
        .WithTags(Tags.Hospital)
        .RequireAuthorization();
    }
}
