namespace PGSH.Domain.Audit;

/// <summary>
/// Un acte qui a eu lieu : qui, quoi, quand, et sur quoi.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Une entrée d'audit est immuable par nature, et rien ne le disait.</b> Cette classe
/// était un sac de propriétés — <c>set</c> public sur chaque membre — donc n'importe quel code
/// tenant l'instance pouvait réécrire l'auteur ou la date après coup. <b>Un registre qui se corrige
/// après coup n'est pas un registre</b> : c'est précisément la propriété pour laquelle il existe. Les
/// accesseurs sont maintenant <c>init</c> au-dessus de champs explicites — l'initialiseur d'objet
/// que la migration et les tests utilisent fonctionne toujours, et rien ne change une entrée
/// <em>ensuite</em>. Même forme que <c>AcademicYear</c> et <c>CnpnVersion</c>, et pour la même
/// raison : écrire le champ directement est exactement ainsi qu'une valeur gardée perd sa garde.</para>
///
/// <para>⚠ <b>Et l'horloge n'est plus dedans.</b> <c>CreatedAt</c> s'initialisait à
/// <c>DateTime.UtcNow</c> dans son propre initialiseur, ce qui rendait l'instant d'un acte
/// intestable et faisait dépendre le domaine de l'heure de la machine — alors que ce dépôt passe
/// partout ailleurs le temps en paramètre (<c>SafePointEvaluator.Evaluate(…, nowUtc)</c>,
/// <c>WorkingDayCalendar</c> « pure — no store, no clock »). <see cref="Record"/> le reçoit ; c'est
/// la couche Application qui tient l'<c>IDateTimeProvider</c>.</para>
///
/// <para>Pas d'héritage d'<c>Entity</c>, délibérément : une entrée de journal n'a pas d'invariant
/// sur des enfants et n'a rien à faire observer. Lever un événement de domaine <em>à propos de
/// l'enregistrement d'un acte</em> serait de la symétrie pour la symétrie.</para>
/// </remarks>
public sealed class AuditLog
{
    private string _action = default!;
    private string _entityType = default!;

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Le <c>sub</c> Keycloak de l'auteur, ou <c>null</c> pour un acte hors session utilisateur
    /// (tâche planifiée, script de maintenance).
    /// </summary>
    /// <remarks>
    /// ⚠ Ce n'est <b>pas</b> une clé étrangère vers <c>User</c> et cela ne doit pas le devenir : une
    /// entrée doit survivre à la suppression de son auteur, et à une restauration de la base sans
    /// son royaume Keycloak. La lecture résout le nom quand elle peut et dit « non résolu » sinon.
    /// </remarks>
    public Guid? PerformedByUserId { get; init; }

    /// <summary>Le code de l'acte — <c>PARTITIONS_ASSIGNED</c>, <c>GROUP_EMPTIED</c>…</summary>
    public string Action
    {
        get => _action;
        init => _action = Required(value, nameof(Action));
    }

    /// <summary>Le type visé : <c>AcademicYear</c>, <c>AcademicGroup</c>, <c>Registration</c>…</summary>
    public string EntityType
    {
        get => _entityType;
        init => _entityType = Required(value, nameof(EntityType));
    }

    public string? EntityId { get; init; }

    /// <summary>Les critères de l'acte, en JSON plat. Voir <c>AuditMetadataJson</c>.</summary>
    public string? Metadata { get; init; }

    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// L'entrée décrivant <paramref name="action"/>, telle qu'elle sera écrite.
    /// </summary>
    /// <param name="performedBy">Le <c>sub</c> Keycloak de l'auteur, ou <c>null</c> hors session.</param>
    /// <param name="nowUtc">
    /// L'instant de l'acte. Passé, jamais lu ici : c'est ce qui rend la date d'une entrée vérifiable
    /// et sort le domaine de l'horloge de la machine.
    /// </param>
    public static AuditLog Record(
        string action,
        string entityType,
        string? entityId,
        string? metadata,
        Guid? performedBy,
        DateTime nowUtc) =>
        new()
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Metadata = metadata,
            PerformedByUserId = performedBy,
            CreatedAt = nowUtc,
        };

    /// <summary>
    /// ⚠ Lève, et c'est le seul endroit du chemin d'audit où c'est acceptable — parce que le défaut
    /// qu'elle attrape est <b>de programmation</b> (une commande déclarant un acte vide), pas une
    /// donnée d'utilisateur. <c>AuditLogVocabularyTests</c> le fait échouer à la compilation des
    /// tests plutôt qu'au clic de quelqu'un ; ceci n'est que le filet en dessous.
    /// </summary>
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                $"Une entrée d'audit sans {name} ne dit rien de ce qui a eu lieu.", name)
            : value;
}
