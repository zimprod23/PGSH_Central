using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Delocalization.Bulk;

namespace PGSH.API.Endpoints.Stages;

/// <summary>
/// The two halves of a mass délocalisation: what it would do, and doing it.
/// </summary>
/// <remarks>
/// The preview is a POST because its selection is a body — whole rosters, named students and a pasted
/// list of identifiers — and not a query string. It writes nothing.
/// </remarks>
public sealed class BulkDelocalization : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("stages/delocalize/bulk/preview", async (
            PreviewBulkDelocalizationQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapPost("stages/delocalize/bulk", async (
            ApplyBulkDelocalizationCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
