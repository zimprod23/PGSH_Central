using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.Services.Delete;

/// <summary>
/// Retire un service du catalogue, une fois que plus rien ne le nomme.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Audité, et il ne l'était pas.</b> Le service est du catalogue year-invariant : le
/// supprimer emporte en cascade ses quotas (<c>ServiceLevelCapacity</c>), l'historique de ses chefs
/// (<c>ServiceChefAssignment</c>) et le rattachement de son personnel. Or accorder un quota et
/// nommer un chef sont des décisions humaines que rien d'autre ne conserve : une fois la ligne
/// partie, le registre est le seul endroit qui puisse encore dire qu'elle a existé. Même asymétrie
/// que <c>STAGE_DELETED</c>.</para>
///
/// <para>Ce que la cascade prend est déposé par le handler via <c>IAuditTrail</c> : la réponse est un
/// <c>204</c> et ne peut rien porter.</para>
/// </remarks>
public sealed record DeleteServiceCommand(int Id) : ICommand, IAuditableCommand
{
    public string AuditAction => "SERVICE_DELETED";
    public string AuditEntityType => "Service";
    public string? AuditEntityId => Id.ToString();
    public string? AuditMetadata => null;
}
