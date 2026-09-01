using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Export;

namespace PGSH.API.Endpoints.Exports;

/// <summary>
/// The post-validation stage record: three sheets — one row per stage attempt, one row per période,
/// and the verdict counts per stage.
///
/// <para><c>onlyEvaluated=true</c> narrows it to the attempts that carry a verdict, for the day the
/// file is a PV rather than a state of play. Left off, the unmarked rows are in the document, which
/// is what makes a missing évaluation visible instead of indistinguishable from a student nobody
/// planned.</para>
/// </summary>
public sealed class StageAssignmentsExport : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("stages/assignments/export", async (
            [AsParameters] GetStageAssignmentsExportQuery query,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);

            return result.Match(
                file => Results.File(file.Content, ExportContentType.Xlsx, file.FileName),
                CustomResults.Problem);
        })
        .WithName("ExportStageAssignments")
        .WithTags(Tags.InternshipAssignments)
        .RequireAuthorization();
    }
}
