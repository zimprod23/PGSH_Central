using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.RotationCycle;

namespace PGSH.API.Endpoints.Stages;

/// <summary>
/// The crossover generator: given the stages that run concurrently and how long each takes, work out
/// which partition is where, and author one shared set of period windows for the whole block.
///
/// <para>Two routes, preview then apply. The apply returns the matrix rather than executing it — feed
/// it to <c>POST stages/macro-plan</c>, which places the cohorts exactly as it does for a matrix
/// ticked by hand.</para>
/// </summary>
public sealed class RotationCycleEndpoint : IEndpoint
{
    public sealed record Request(
        IReadOnlyList<int> StageIds,
        int PeriodsPerStage,
        IReadOnlyList<DateWindow> Windows,
        int? AcademicYearId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("levels/{levelId:int}/rotation-cycle/preview", async (
            int levelId,
            Request request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new PreviewRotationCycleQuery(
                levelId, request.StageIds, request.PeriodsPerStage, request.Windows,
                request.AcademicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("PreviewRotationCycle")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapPost("levels/{levelId:int}/rotation-cycle", async (
            int levelId,
            Request request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new ApplyRotationCycleCommand(
                levelId, request.StageIds, request.PeriodsPerStage, request.Windows,
                request.AcademicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("ApplyRotationCycle")
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
