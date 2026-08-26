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
    /// <summary>
    /// <paramref name="Stages"/> carries each stage's own period count — they need not be equal.
    /// <paramref name="Windows"/> is the block's axis at its finest granularity, entered once.
    /// </summary>
    public sealed record Request(
        IReadOnlyList<RotationStage> Stages,
        IReadOnlyList<DateWindow> Windows,
        int? AcademicYearId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // What this promotion is laid out on right now, so reopening the screen shows the block
        // instead of an empty form. Read from the axis on disk, not from the last request.
        app.MapGet("levels/{levelId:int}/rotation-cycle", async (
            int levelId,
            int? academicYearId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetRotationCycleQuery(levelId, academicYearId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetRotationCycle")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapPost("levels/{levelId:int}/rotation-cycle/preview", async (
            int levelId,
            Request request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new PreviewRotationCycleQuery(
                levelId, request.Stages, request.Windows, request.AcademicYearId), ct);

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
                levelId, request.Stages, request.Windows, request.AcademicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("ApplyRotationCycle")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        // Removing the block, as its own act. Replacing an axis is not undoing one, so a block entered
        // by mistake had no way back except deleting each stage's slots by hand.
        app.MapDelete("levels/{levelId:int}/rotation-cycle", async (
            int levelId,
            int[] stageIds,
            int? academicYearId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new DeleteRotationCycleCommand(levelId, stageIds, academicYearId), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("DeleteRotationCycle")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        // Lays the axis out from one start date. Server-side because the working-day count needs the
        // holiday table, which no browser has.
        app.MapGet("stages/axis-windows", async (
            [AsParameters] GenerateAxisWindowsQuery query,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GenerateAxisWindows")
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
