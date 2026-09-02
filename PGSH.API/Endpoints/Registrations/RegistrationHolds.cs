using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.Holds;

namespace PGSH.API.Endpoints.Registrations;

/// <summary>
/// The signalements: registrations PGSH has created but will not plan until somebody settles them.
///
/// <para><b>This is the page the whole mechanism exists for.</b> Holding a registration is half an
/// answer — the other half is walking the list one student at a time and clearing it, which is what
/// « on les ajuste manuellement depuis l'application » means. Without these two routes the flag is a
/// silent exclusion, i.e. the failure it was built to remove.</para>
///
/// <para>⚠ <b>There is no bulk release, deliberately.</b> The réinscription roll raises them by the
/// thousand and they come off one by one, because each is a different question: has this évaluation
/// been keyed in, did this student really defend, is this one simply coming back late. A « tout
/// lever » button would undo in one click the only thing that made a 1 267-row inference safe to
/// record in the first place.</para>
/// </summary>
public sealed class RegistrationHolds : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Paginated and year-scoped: one roll raises ~1 450 of these, which is the unbounded-list
        // shape that has taken this browser down four times. An omitted year is the current one.
        app.MapGet("registrations/holds", async (
            [AsParameters] GetRegistrationHoldsQuery query,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetRegistrationHolds")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();

        // The note is required by the command's validator, not merely encouraged: the hold row
        // survives its own release precisely so the file can say who cleared the student and on what.
        app.MapPost("registrations/holds/{id:guid}/release", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ReleaseRegistrationHoldCommand(id, request.ReleaseNote), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("ReleaseRegistrationHold")
        .WithTags(Tags.Registrations)
        .RequireAuthorization();
    }

    public sealed record Request(string ReleaseNote);
}
