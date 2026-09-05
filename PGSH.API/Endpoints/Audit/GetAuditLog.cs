using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Audit;

namespace PGSH.API.Endpoints.Audit;

/// <summary>
/// « Qui a fait ça, et quand ? » — la première route capable de <b>relire</b> <c>AuditLogs</c>.
///
/// <para>Trente-cinq commandes y écrivaient et rien ne pouvait l'ouvrir : la table était en écriture
/// seule, consultable seulement en interrogeant la base à la main. Voir
/// <see cref="GetAuditLogQuery"/>.</para>
/// </summary>
public sealed class GetAuditLog : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("audit-log", async (
            [AsParameters] GetAuditLogQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetAuditLog")
        .WithTags(Tags.Audit)
        .RequireAuthorization();
    }
}
