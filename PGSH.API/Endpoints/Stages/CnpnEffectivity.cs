using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cnpn.Effectivity;
using PGSH.Domain.Common.Utils;

namespace PGSH.API.Endpoints.Stages;

/// <summary>
/// « Ce texte régit tel niveau à partir de telle année » — the second half of who a CNPN binds,
/// alongside the intake year on the text itself.
///
/// <para>Intake governs the promotion arriving; these rules govern the promotions already in the
/// building, which is what « la 3ᵉ année de 2026-2027 et en dessous » actually means. They are read
/// as each registration is created, so authoring one before the réinscription is all that is
/// normally needed.</para>
/// </summary>
public sealed class CnpnEffectivity : IEndpoint
{
    public sealed record CreateRequest(int LevelId, int FromAcademicYearId, string? Note);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cnpn-effectivity", async (
            int? cnpnVersionId, AcademicProgram? program, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCnpnEffectivitiesQuery(cnpnVersionId, program), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapPost("cnpn-versions/{cnpnVersionId:int}/effectivity", async (
            int cnpnVersionId, CreateRequest request, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateCnpnEffectivityCommand(
                cnpnVersionId, request.LevelId, request.FromAcademicYearId, request.Note);

            var result = await sender.Send(command, ct);
            return result.Match(
                id => Results.Created($"cnpn-effectivity/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        // Returns how many registrations the rule had already stamped. They are left alone: removing
        // the rule changes which text the *next* registration resolves to, never what a student has
        // been studying against.
        app.MapDelete("cnpn-effectivity/{id:int}", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteCnpnEffectivityCommand(id), ct);
            return result.Match(
                registrationsGoverned => Results.Ok(new { registrationsGoverned }),
                CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        // Only needed when the rule was authored *after* the registrations it should have governed
        // were created. Preview then apply, the apply echoing back the count it was shown.
        app.MapGet("cnpn-effectivity/{id:int}/apply/preview", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new PreviewCnpnEffectivityQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapPost("cnpn-effectivity/{id:int}/apply", async (
            int id, ApplyRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ApplyCnpnEffectivityCommand(id, request.ConfirmedMoveCount), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }

    public sealed record ApplyRequest(int ConfirmedMoveCount);
}
