using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Repartition;

namespace PGSH.API.Endpoints.Levels;

public sealed class Repartition : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // academicYearId stays nullable on the wire and is resolved to the current year server-side:
        // a level's cohorts exist per year, so omitting it must mean "this year" and never "every
        // year the level ever ran".
        app.MapGet("levels/{levelId:int}/repartition",
            async (int levelId, int? academicYearId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetLevelRepartitionQuery(levelId, academicYearId), ct);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Levels")
            .WithName("GetLevelRepartition")
            .RequireAuthorization();
    }
}
