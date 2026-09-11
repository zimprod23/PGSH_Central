using MediatR;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Domain.Audit;
using PGSH.SharedKernel;

namespace PGSH.Application.Behaviors;

/// <summary>
/// Écrit l'entrée de journal d'une commande auditable.
/// </summary>
/// <remarks>
/// ⚠ <b>La ligne est ajoutée au contexte <i>avant</i> le handler et n'est validée que par le
/// <c>SaveChanges</c> de celui-ci.</b> C'est ce qui fait qu'un acte <b>refusé n'écrit rien</b> — le
/// registre reste la liste de ce qui a eu lieu, pas des tentatives — et que l'entrée est écrite dans
/// la même unité de travail que l'acte, donc jamais sans lui. Propriété juste et fragile, puisqu'elle
/// tient à l'ordre de deux lignes : épinglée par
/// <c>AuditLogEndpointTests.A_refused_act_writes_no_entry</c>.
/// </remarks>
public sealed class AuditLogPipelineBehavior<TRequest, TResponse>(
    AuditTrail trail,
    IUserContext userContext,
    IDateTimeProvider clock)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IAuditableCommand auditable)
        {
            // L'instant vient de l'horloge injectée, pas de l'entité : une date que le domaine se
            // donne à lui-même n'est ni vérifiable ni déplaçable.
            //
            // Ouverte sur la piste plutôt qu'ajoutée au contexte directement : le handler peut
            // ensuite y déposer ce que l'acte a réellement emporté, ce qu'une commande ne peut pas
            // savoir d'elle-même. Voir IAuditTrail.
            trail.Open(AuditLog.Record(
                auditable.AuditAction,
                auditable.AuditEntityType,
                auditable.AuditEntityId,
                auditable.AuditMetadata,
                TryGetUserId(),
                clock.UtcNow));
        }

        return await next();
    }

    /// <summary>
    /// ⚠ <c>IUserContext.UserId</c> lève hors d'une requête HTTP — un acte lancé par le planificateur
    /// n'a personne à nommer. « Aucun auteur » est un fait que le journal enregistre (« système »),
    /// pas une raison de faire échouer l'acte.
    /// </summary>
    private Guid? TryGetUserId()
    {
        try { return userContext.UserId; }
        catch { return null; }
    }
}
