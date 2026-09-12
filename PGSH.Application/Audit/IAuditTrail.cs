using PGSH.SharedKernel;

namespace PGSH.Application.Audit;

/// <summary>
/// Ce que l'acte en cours a réellement fait, ajouté à l'entrée qu'il est déjà en train d'écrire.
/// </summary>
/// <remarks>
/// <para><b>Pourquoi cela ne pouvait pas venir de la commande.</b> <c>IAuditableCommand</c> décrit ce
/// qui a été <i>demandé</i> — un stage, une année, une cohorte — et c'est tout ce qu'une commande
/// peut savoir. Or sur un acte destructeur la question qu'on pose au registre trois mois plus tard
/// n'est pas « qui a cliqué » mais « combien cela a-t-il emporté » : « Réinitialiser les cohortes »
/// sur une promotion vide et la même sur une promotion publiée écrivent exactement la même ligne, et
/// ce sont deux événements sans rapport. ⚠ <b>C'est la règle « dire ce que le blanc veut dire »
/// appliquée au journal</b> : un code d'acte qui recouvre les deux ne peut pas les distinguer.</para>
///
/// <para>⚠ <b>Et l'entrée n'est pas encore un fait du registre quand ceci l'écrit.</b>
/// <c>AuditLogPipelineBehavior</c> la met en attente <i>avant</i> le handler et c'est le
/// <c>SaveChanges</c> du handler qui la valide — d'où la propriété qu'un acte refusé n'écrit rien.
/// Compléter une insertion en attente n'est donc pas corriger le registre après coup : c'est finir la
/// phrase avant de la valider. L'immuabilité d'<c>AuditLog</c> reste entière — l'implémentation
/// <i>remplace</i> l'entité en attente, elle n'en modifie aucune.</para>
///
/// <para>⚠ <b>À appeler avant le <c>SaveChangesAsync</c> de l'acte</b>, sinon l'entrée est déjà
/// validée et le complément coûte un DELETE suivi d'un INSERT au lieu d'un seul INSERT. Le résultat
/// écrit est le même ; c'est l'ordre qui est propre. Épinglé par les tests de bout en bout de chaque
/// acte.</para>
///
/// <para>✅ <b>Et cela compose désormais avec une unité de travail atomique — par
/// <see cref="IAuditTrail.RunAtomicallyAsync{T}"/>, jamais par
/// <c>IApplicationDbContext.ExecuteAtomicallyAsync</c> directement.</b> Corrigé le 12/09/2026.
/// L'enveloppe vide le change tracker à chaque nouvelle tentative, donc l'entrée mise en attente
/// <i>avant</i> la transaction disparaît avec lui. Elle relevait autrefois les entités concernées à
/// l'entrée et les remettait telles quelles, ce qui ne pouvait pas marcher ici : le constat
/// <b>remplace</b> l'entité en attente, si bien que la tentative suivante remettait l'entrée
/// <i>sans son constat</i> pendant que la piste tenait la remplaçante — deux lignes pour un acte.
/// C'est la piste, seule, qui sait quelle entrée est la bonne, donc c'est elle qui la remet.</para>
/// </remarks>
public interface IAuditTrail
{
    /// <summary>
    /// Exécute <paramref name="operation"/> comme <b>une</b> transaction — elle atterrit entière ou
    /// pas du tout — en gardant l'entrée du registre attachée à cette unité de travail.
    /// </summary>
    /// <remarks>
    /// <para><b>C'est la seule façon dont un acte audité ouvre une transaction.</b>
    /// <c>IApplicationDbContext.ExecuteAtomicallyAsync</c> reste pour les actes qui n'écrivent rien
    /// au registre : rien n'y est mis en attente avant le handler, donc rien n'y est à remettre.</para>
    ///
    /// <para>⚠ Sans effet sur l'entrée quand l'acte n'est pas auditable — la piste n'a alors rien
    /// ouvert — donc un collaborateur partagé peut envelopper son travail sans savoir qui l'appelle,
    /// exactement comme il appelle <see cref="RecordOutcome"/> sans le savoir.</para>
    ///
    /// <para>⚠ Un <c>Result</c> en échec annule la transaction, comme dans l'enveloppe sous-jacente :
    /// un refus rendu à mi-parcours est le même état partiel qu'une connexion coupée.</para>
    /// </remarks>
    Task<Result<T>> RunAtomicallyAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajoute <paramref name="fields"/> aux métadonnées de l'entrée en attente. Sans effet si l'acte
    /// n'est pas auditable — un collaborateur partagé n'a pas à savoir lequel de ses appelants l'est.
    /// </summary>
    void RecordOutcome(params (string Key, object? Value)[] fields);
}
