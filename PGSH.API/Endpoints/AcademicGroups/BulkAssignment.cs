using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.BulkAssignment;

namespace PGSH.API.Endpoints.AcademicGroups;

/// <summary>
/// The two halves of a nominative roster assignment: what it would do, and doing it.
/// </summary>
/// <remarks>
/// The preview is a POST because its selection is a body — whole rosters, named students and a pasted
/// list of identifiers — and not a query string. It writes nothing.
/// </remarks>
public sealed class BulkAssignment : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("groups/assign/bulk/preview", async (
            PreviewBulkRosterAssignmentQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Groups)
        .RequireAuthorization();

        app.MapPost("groups/assign/bulk", async (
            ApplyBulkRosterAssignmentCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
