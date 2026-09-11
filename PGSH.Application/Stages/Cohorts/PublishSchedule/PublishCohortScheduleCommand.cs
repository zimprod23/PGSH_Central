using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

/// <summary>
/// Materialises one cohorte's planned grid into the périodes its students will actually serve.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Audité, et il ne l'était pas — alors que « Dépublier » l'est depuis la phase 20.</b> Le
/// registre tenait donc le défaire sans le faire : la seule trace d'une publication était l'absence
/// de sa dépublication. Or c'est <i>cet</i> acte qui crée les <c>ServicePeriod</c>, c'est-à-dire tout
/// ce que les chefs notent et tout ce que les présences visent.</para>
///
/// <para>⚠ <b>Et <see cref="AllowOverCapacity"/> est une décision, pas un paramètre.</b> Un service
/// peut refuser d'être dépassé (<c>Service.AllowsOverCapacity</c>) ; passer outre est le geste que
/// quelqu'un pose sciemment contre un refus, et c'est exactement ce qu'on vient demander au registre
/// trois mois plus tard. Il voyage donc avec l'entrée.</para>
/// </remarks>
public sealed record PublishCohortScheduleCommand(int CohortId, bool AllowOverCapacity = false)
    : ICommand, IAuditableCommand
{
    public string AuditAction => "COHORT_SCHEDULE_PUBLISHED";
    public string AuditEntityType => "Cohort";
    public string? AuditEntityId => CohortId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(("allowOverCapacity", AllowOverCapacity));
}
