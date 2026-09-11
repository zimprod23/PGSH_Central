using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Audit;

namespace PGSH.Application.Audit;

/// <summary>
/// L'entrée que l'acte de la requête en cours est en train d'écrire.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Publique, mais <see cref="Open"/> est <c>internal</c>.</b> Le behavior a besoin
/// d'ouvrir une entrée, un handler seulement de la compléter, et c'est la seule asymétrie qui
/// compte : personne hors de cette couche ne décide qu'un acte est audité. Le type est public parce
/// qu'<c>AuditLogPipelineBehavior</c> l'est — un paramètre ne peut pas être moins accessible que la
/// méthode qui le prend.</para>
///
/// <para>Portée requête : <see cref="Open"/> est appelé par <c>AuditLogPipelineBehavior</c>, une fois,
/// avant le handler ; <see cref="RecordOutcome"/> l'est par le handler, autant de fois qu'il a des
/// choses à dire. Hors d'un acte auditable rien n'est ouvert et tout appel est sans effet, ce qui
/// laisse un collaborateur partagé — <c>StudentAffectationService</c>, un publisher — s'en servir sans
/// savoir qui l'appelle.</para>
///
/// <para>⚠ <b>Le remplacement plutôt que la mutation, et ce n'est pas un détour.</b> <c>AuditLog</c>
/// est immuable par construction et doit le rester : « un registre qui se corrige après coup n'est
/// pas un registre ». Tant que l'insertion est en attente, la remplacer n'écrit rien de faux — EF
/// détache l'entité <c>Added</c> retirée et n'émettra que le nouvel INSERT. La propriété gardée est
/// qu'aucun code ne tient une entrée dont il puisse réécrire l'auteur ou la date : ici la seule
/// référence appartient à cette classe, et seules les <i>métadonnées</i> s'enrichissent.</para>
/// </remarks>
public sealed class AuditTrail(IApplicationDbContext dbContext) : IAuditTrail
{
    private AuditLog? _pending;

    /// <summary>
    /// Met l'entrée de l'acte en attente d'écriture. ⚠ Ajoutée au contexte ici et validée par le
    /// <c>SaveChanges</c> du handler : c'est ce qui fait qu'un acte refusé n'écrit rien.
    /// </summary>
    internal void Open(AuditLog entry)
    {
        _pending = entry;
        dbContext.AuditLogs.Add(entry);
    }

    /// <inheritdoc />
    public void RecordOutcome(params (string Key, object? Value)[] fields)
    {
        if (_pending is null || fields.Length == 0)
            return;

        // L'identifiant du remplaçant est neuf, et cela n'a aucune conséquence : rien ne référence
        // une entrée de journal, et celle-ci n'est pas encore écrite.
        var completed = AuditLog.Record(
            _pending.Action,
            _pending.EntityType,
            _pending.EntityId,
            AuditMetadataJson.Merge(_pending.Metadata, fields),
            _pending.PerformedByUserId,
            _pending.CreatedAt);

        dbContext.AuditLogs.Remove(_pending);
        dbContext.AuditLogs.Add(completed);

        _pending = completed;
    }
}
