using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.RotationCycle;

namespace PGSH.API.Endpoints.Stages;

/// <summary>
/// Reposer l'axe d'une promotion sur son calendrier — le rattrapage d'une fenêtre déclarée après que
/// la grille a été posée.
///
/// <para>Deux routes, aperçu puis application. L'aperçu n'écrit rien ; l'application confirme
/// <b>deux</b> nombres et tient dans une transaction.</para>
/// </summary>
public sealed class AxisRelayEndpoint : IEndpoint
{
    /// <param name="ConfirmedSlotCount">Ce que l'aperçu annonçait pour la grille.</param>
    /// <param name="ConfirmedPeriodCount">Ce qu'il annonçait pour les dossiers.</param>
    public sealed record Request(
        int ConfirmedSlotCount,
        int ConfirmedPeriodCount,
        int? AcademicYearId,
        int? FromPeriodNumber);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // ⚠ Les paramètres facultatifs sont liés en nullable : un type valeur obligatoire en query
        // string lève dans EndpointMiddleware, donc avant le validateur, et l'écran ne reçoit qu'un
        // 400 nu.
        app.MapGet("levels/{levelId:int}/axis-relay/preview", async (
            int levelId,
            int? academicYearId,
            int? fromPeriodNumber,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new PreviewAxisRelayQuery(levelId, academicYearId, fromPeriodNumber), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("PreviewAxisRelay")
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapPost("levels/{levelId:int}/axis-relay", async (
            int levelId,
            Request request,
            ISender sender,
            CancellationToken ct) =>
        {
            var command = new ApplyAxisRelayCommand(
                levelId,
                request.ConfirmedSlotCount,
                request.ConfirmedPeriodCount,
                request.AcademicYearId,
                request.FromPeriodNumber);

            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("ApplyAxisRelay")
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
