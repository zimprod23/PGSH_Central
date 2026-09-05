using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Audit;

/// <summary>
/// « Qui a fait ça, et quand ? » — le journal des actes enregistrés.
///
/// <para><b>Pourquoi cette lecture existe.</b> Trente-cinq commandes écrivaient dans
/// <c>AuditLogs</c> depuis des mois et <b>rien ne pouvait le relire</b> : ni route, ni écran. La
/// table était en écriture seule, donc la trace n'était consultable qu'en interrogeant la base à la
/// main. Le 02/09/2026 la question s'est posée pour de vrai — 66 rosters étaient apparus sur la 7ᵉ
/// MED et personne ne pouvait dire d'où — et c'est cette moitié-là qui manquait le plus : ajouter
/// des lignes à un registre que personne ne peut ouvrir n'aurait rien répondu.</para>
/// </summary>
/// <remarks>
/// ⚠ <b>Volontairement non scopée par année universitaire.</b> Une entrée d'audit est datée d'une
/// horloge, pas d'une année académique : elle enregistre un <i>geste</i>, et le geste qui touche
/// 2026-2027 peut très bien avoir été fait en juillet. Le filtre est donc une plage de dates, et
/// c'est la seule lecture de ce dépôt où l'absence d'année n'est pas une erreur — la règle
/// « une année omise vaut l'année en cours » parle des lectures scopées *par* l'année, pas de
/// celle-ci.
/// </remarks>
/// <param name="Action">Un code d'acte exact (<c>PARTITIONS_ASSIGNED</c>…), tel qu'il est stocké.</param>
/// <param name="EntityType">Le type visé (<c>AcademicYear</c>, <c>AcademicGroup</c>, <c>Registration</c>…).</param>
/// <param name="From">Instant UTC, <b>inclus</b>.</param>
/// <param name="To">
/// Instant UTC, <b>exclu</b>.
///
/// <para>⚠ <b>Ce sont des instants et non des jours, et c'est le correctif d'un défaut mesuré.</b>
/// Les bornes ont d'abord été des <c>DateOnly</c> résolus à minuit UTC — alors que l'écran affiche
/// l'heure dans le fuseau du navigateur. Une entrée écrite le 02/09 à 22:16 UTC s'affiche
/// « 03/09 00:16 » à Casablanca, et un filtre « du 3 au 3 » la faisait disparaître : la date lue et
/// la date filtrée n'étaient pas la même. Trouvé en pilotant l'écran le 04/09/2026, sur des données
/// réelles.</para>
///
/// <para><b>La journée appartient au calendrier de celui qui lit</b>, donc c'est le client qui la
/// convertit : « au 3 inclus » devient « &lt; 4 septembre 00:00 <i>locale</i> », envoyé en UTC. Le
/// serveur ne suppose alors aucun fuseau — ce qu'il ne pourrait de toute façon pas faire
/// correctement, le Maroc basculant à UTC+0 pendant le ramadan.</para>
/// </param>
public sealed record GetAuditLogQuery(
    string? Action = null,
    string? EntityType = null,
    string? EntityId = null,
    DateTime? From = null,
    DateTime? To = null,
    int PageNumber = 1,
    int PageSize = GetAuditLogQuery.DefaultPageSize) : IQuery<AuditLogPage>
{
    public const int DefaultPageSize = 50;

    /// <summary>
    /// ⚠ Une taille de page nulle veut dire « non précisée », jamais « une ligne » :
    /// <c>ToPaginatedResponseAsync</c> remonte un 0 <em>vers</em> 1, donc un journal de 40 000
    /// entrées répondrait par une seule sans que rien ne le dise.
    /// </summary>
    public int EffectivePageNumber => PageNumber > 0 ? PageNumber : 1;

    public int EffectivePageSize => PageSize > 0 ? PageSize : DefaultPageSize;
}

/// <param name="Actions">
/// Les codes d'actes réellement présents, avec leur nombre — les puces avec lesquelles on filtre.
/// ⚠ Comptés sur <b>tout</b> le journal et non sur la fenêtre courante : ce sont les valeurs
/// disponibles, et les réduire au filtre actif supprimerait le chemin du retour.
/// </param>
public sealed record AuditLogPage(
    PaginatedResponse<AuditLogEntryResponse> Entries,
    IReadOnlyList<AuditActionCount> Actions,
    int TotalEntries);

public sealed record AuditActionCount(string Action, int Count);

/// <param name="PerformedBy">
/// Le nom de l'auteur, ou <c>null</c> quand il ne peut pas être résolu. ⚠ <b>Null ne veut pas dire
/// « personne »</b> : l'identifiant stocké est le <c>sub</c> Keycloak, et il ne correspond à aucun
/// <c>User</c> local si le compte a été supprimé, ou si la base a été restaurée sans son royaume
/// Keycloak (<c>Backups:KeycloakRealmCovered</c> est <c>false</c>). Le brut est donc renvoyé à côté.
/// </param>
/// <param name="PerformedByUserId">
/// L'identifiant tel qu'il est écrit dans la table. Renvoyé même quand le nom est résolu : c'est la
/// seule chose qui reste vraie si l'annuaire change, et c'est ce qu'on recherche dans les logs.
/// </param>
public sealed record AuditLogEntryResponse(
    Guid Id,
    string Action,
    string EntityType,
    string? EntityId,
    string? Metadata,
    DateTime CreatedAt,
    Guid? PerformedByUserId,
    string? PerformedBy);
