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
/// <para>⚠ <b>Et jamais dans un handler enveloppé par <c>ExecuteAtomicallyAsync</c>.</b> Sur une
/// nouvelle tentative, celui-ci vide le change tracker puis <i>re-stage</i> les entités qu'il avait
/// relevées avant la transaction — dont l'entrée telle qu'ouverte, sans son constat. La piste, elle,
/// tient la remplaçante : elle retirerait une entité détachée et en ajouterait une troisième, ce qui
/// donne deux lignes pour un acte. Les deux mécanismes répondent au même besoin par deux chemins, et
/// il faut en choisir un : un handler qui écrit par <c>ExecuteDelete</c>/<c>ExecuteUpdate</c> —
/// hors unité de travail — termine par un <c>SaveChangesAsync</c> qui valide l'entrée, et se passe
/// de l'enveloppe.</para>
/// </remarks>
public interface IAuditTrail
{
    /// <summary>
    /// Ajoute <paramref name="fields"/> aux métadonnées de l'entrée en attente. Sans effet si l'acte
    /// n'est pas auditable — un collaborateur partagé n'a pas à savoir lequel de ses appelants l'est.
    /// </summary>
    void RecordOutcome(params (string Key, object? Value)[] fields);
}
