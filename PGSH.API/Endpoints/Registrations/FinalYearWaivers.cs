using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.FinalYear;

namespace PGSH.API.Endpoints.Registrations;

/// <summary>
/// Dérogations d'entrée en dernière année — the nominative exception to « on ne commence pas la
/// dernière année tant que tout ce qui précède n'est pas validé ».
///
/// <para>Granted before the réinscription and consumed by it. The rollover refuses the student
/// without one, and reports him as <c>FinalYearBlocked</c> so somebody decides between revalidating
/// the stage and excusing it.</para>
/// </summary>
public sealed class FinalYearWaivers : IEndpoint
{
    public sealed record GrantRequest(Guid StudentId, int AcademicYearId, string Reason);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("final-year-waivers", async (
            int? academicYearId, Guid? studentId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFinalYearWaiversQuery(academicYearId, studentId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        app.MapPost("final-year-waivers", async (
            GrantRequest request, ISender sender, CancellationToken ct) =>
        {
            var command = new GrantFinalYearWaiverCommand(
                request.StudentId, request.AcademicYearId, request.Reason);

            var result = await sender.Send(command, ct);
            return result.Match(
                id => Results.Created($"final-year-waivers/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        // Refused once the registration it permitted exists — the waiver is that year's justification.
        app.MapDelete("final-year-waivers/{id:guid}", async (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RevokeFinalYearWaiverCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        // What the gate reads, so a screen can show the same list before anyone decides between
        // revalidating a stage and excusing it.
        app.MapGet("students/{studentId:guid}/outstanding-stages", async (
            Guid studentId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOutstandingStagesQuery(studentId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Registrations)
        .RequireAuthorization();
    }
}
