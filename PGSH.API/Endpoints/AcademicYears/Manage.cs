using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicYears.Manage;

namespace PGSH.API.Endpoints.AcademicYears;

/// <summary>
/// The three acts a year needed and did not have: correct it, designate it as « l'année en cours »,
/// and remove one created by mistake.
///
/// <para>Designating is deliberately its own route rather than a field on the update. A year is
/// normally created months before it becomes current, and folding the two together is what left
/// « créer une année » as the only way to move the flag — so the flag moved as a side effect of
/// something else.</para>
/// </summary>
public sealed class Manage : IEndpoint
{
    public sealed record UpdateRequest(string Label, DateOnly StartDate, DateOnly EndDate);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("academic-years/{id:int}", async (
            int id, UpdateRequest request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateAcademicYearCommand(
                id, request.Label, request.StartDate, request.EndDate);

            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("UpdateAcademicYear")
        .WithTags("AcademicYears")
        .RequireAuthorization();

        app.MapPost("academic-years/{id:int}/current", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new SetCurrentAcademicYearCommand(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("SetCurrentAcademicYear")
        .WithTags("AcademicYears")
        .RequireAuthorization();

        app.MapDelete("academic-years/{id:int}", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteAcademicYearCommand(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("DeleteAcademicYear")
        .WithTags("AcademicYears")
        .RequireAuthorization();
    }
}
