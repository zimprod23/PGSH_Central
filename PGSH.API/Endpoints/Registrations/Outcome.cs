using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.Outcome;
using PGSH.Domain.Registrations;

namespace PGSH.API.Endpoints.Registrations;

/// <summary>
/// One student's year verdict, recorded or withdrawn without a file. The déliberation canvas closes a
/// promotion; these two close and re-open a single registration, which is what a late jury, a
/// corrected PV or an abandon notified in November actually needs.
/// </summary>
public sealed class Outcome : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("registrations/{id:guid}/outcome", async (
            Guid id,
            RecordRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new RecordRegistrationOutcomeCommand(id, request.Outcome, request.Motif), ct);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithName("RecordRegistrationOutcome")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        app.MapPost("registrations/{id:guid}/outcome/reopen", async (
            Guid id,
            ReopenRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new ReopenRegistrationYearCommand(id, request.Reason), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("ReopenRegistrationYear")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();
    }

    public sealed record RecordRequest(RegistrationStatus Outcome, string? Motif);

    public sealed record ReopenRequest(string? Reason);
}
