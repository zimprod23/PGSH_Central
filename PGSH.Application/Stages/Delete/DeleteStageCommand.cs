using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Delete;

/// <summary>
/// Removes a stage from the catalogue.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Audité, et il ne l'était pas.</b> Le stage est du catalogue year-invariant : le
/// supprimer emporte en cascade ses <c>StageSlots</c> — les créneaux de <b>toutes</b> les années — et
/// ses <c>StageAllowedServices</c>, dont l'ordre et les modes « Réservé ». Or poser un créneau et
/// ordonner les services sont eux-mêmes audités (<c>STAGE_SLOT_CREATED</c>,
/// <c>STAGE_SERVICE_ORDER_SET</c>) : le registre tenait donc la construction sans la destruction, sur
/// la seule chose qui puisse encore dire ce que la cascade a emporté. Même asymétrie que
/// publier/dépublier.</para>
///
/// <para>Ce que la cascade prend est déposé par le handler via <c>IAuditTrail</c> : la réponse est un
/// <c>204</c> et ne peut rien porter, et ces nombres ne se relisent nulle part ensuite.</para>
/// </remarks>
public record DeleteStageCommand(int StageId) : ICommand, IAuditableCommand
{
    public string AuditAction => "STAGE_DELETED";
    public string AuditEntityType => "Stage";
    public string? AuditEntityId => StageId.ToString();
    public string? AuditMetadata => null;
}
