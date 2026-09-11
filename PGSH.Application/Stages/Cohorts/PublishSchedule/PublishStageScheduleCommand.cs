using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

/// <summary>
/// Materialises a whole stage's planned grid, for one year, into the périodes its cohortes will
/// serve — the act <c>UnpublishStageScheduleCommand</c> undoes.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Audité, et il ne l'était pas, alors que son inverse l'est.</b> Le registre tenait donc
/// le défaire sans le faire, sur l'acte qui crée les <c>ServicePeriod</c> d'une promotion entière :
/// la seule trace d'une publication était l'absence de sa dépublication.</para>
///
/// <para>⚠ <b>La portée voyage avec l'entrée</b>, exactement comme sur la dépublication : « publier
/// le stage » et « publier la partition B, périodes 3 à 5 » se lisent autrement, et sans la mention
/// la seconde ressemblerait à la première jouée à moitié. <see cref="AllowOverCapacity"/> aussi —
/// c'est le geste posé sciemment contre un service qui refuse d'être dépassé. L'année réellement
/// touchée est déposée par le handler, seul à l'avoir résolue.</para>
/// </remarks>
public sealed record PublishStageScheduleCommand(
    int StageId,
    int? AcademicYearId = null,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null,
    bool AllowOverCapacity = false) : ICommand<PublishResult>, IAuditableCommand
{
    public string AuditAction => "STAGE_SCHEDULE_PUBLISHED";
    public string AuditEntityType => "Stage";
    public string? AuditEntityId => StageId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(
        ("partitionLabels", PartitionLabels is { Count: > 0 } l ? string.Join(", ", l) : null),
        ("periodNumbers", PeriodNumbers is { Count: > 0 } p ? string.Join(", ", p) : null),
        ("allowOverCapacity", AllowOverCapacity));
}
