using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.UnpublishSchedule;

/// <summary>
/// Undoes a whole stage's publication in one act, the way
/// <c>PublishStageScheduleCommand</c> does the opposite.
///
/// <para>⚠ <b>It was a client-side loop, and that is what made « Dépublier toutes » slow.</b> One
/// HTTP request per cohorte, awaited in sequence — 134 for the 3ᵉ MED — each one loading its
/// assignments with their périodes and evaluations, and each one invalidating the stage's cache tag
/// so the page refetched a 134-row list after every single request. The lag was the refetch storm,
/// not the deletion. And because <c>errorMiddleware</c> toasts every rejected mutation, a stage with
/// several rotations underway answered with one red toast per cohorte, arriving one at a time as the
/// loop ground on.</para>
///
/// <para>⚠ <b>There is deliberately no <c>Force</c>.</b> Forcing destroys marks and attendance, and
/// the act that may do that is the per-cohorte « Dépublier », which names what <i>that</i> cohorte
/// would lose and asks a second time. A bulk sweep must never become the way round it — the same
/// reason <c>AllowOverCapacity</c> had to stop waiving admissibility, and the same reason
/// <c>EmptyAllYearGroupsCommand</c> carries no <c>DropAffectations</c>.</para>
/// </summary>
public sealed record UnpublishStageScheduleCommand(
    int StageId,
    int? AcademicYearId = null,
    IReadOnlyList<string>? PartitionLabels = null)
    : ICommand<UnpublishStageResult>, IAuditableCommand
{
    public string AuditAction => "STAGE_SCHEDULE_UNPUBLISHED";
    public string AuditEntityType => "Stage";
    public string? AuditEntityId => StageId.ToString();

    /// <summary>
    /// ⚠ Les partitions visées, parce que cet acte est <b>scopable</b> : « dépublier le stage » et
    /// « dépublier la partition B du stage » se lisent autrement dans le registre, et sans cette
    /// mention la seconde ressemblerait à la première jouée à moitié. L'année réellement touchée est
    /// écrite par le handler, seul à l'avoir résolue.
    /// </summary>
    public string? AuditMetadata => PartitionLabels is { Count: > 0 } labels
        ? AuditMetadataJson.Of(("partitionLabels", string.Join(", ", labels)))
        : null;
}

/// <param name="CohortsUnpublished">Cohortes whose grid-linked périodes were removed.</param>
/// <param name="PeriodsRemoved">Those périodes.</param>
/// <param name="AdHocPeriodsKept">
/// Périodes left untouched because no cell produced them — imported history, délocalisations,
/// revalidations. None came from a répartition and none can be recreated by publishing one.
/// </param>
/// <param name="CohortsSkippedUnderway">
/// Cohortes left alone because their rotation has begun. ⚠ <b>Reported, never swept.</b> Undoing
/// those is the per-cohorte act, which states what each one costs.
/// </param>
/// <param name="PeriodsUnderway">Grid-linked périodes on those cohortes.</param>
/// <param name="EvaluationsAtRisk">Chefs' marks standing on them.</param>
/// <param name="AttendanceDaysAtRisk">Journées de présence recorded against them.</param>
/// <param name="HeaviestSkipped">
/// The few skipped cohortes carrying the most, so the sentence names something the operator can act
/// on rather than only a total. Capped — a stage-wide list is what the aggregate exists to replace.
/// </param>
public sealed record UnpublishStageResult(
    int CohortsUnpublished,
    int PeriodsRemoved,
    int AdHocPeriodsKept,
    int CohortsSkippedUnderway,
    int PeriodsUnderway,
    int EvaluationsAtRisk,
    int AttendanceDaysAtRisk,
    IReadOnlyList<SkippedCohort> HeaviestSkipped)
{
    /// <summary>
    /// ⚠ Zero cohortes unpublished has two causes calling for opposite acts — nothing was published
    /// (there is nothing to undo) versus everything has begun (undo them one at a time, having read
    /// what each costs). The two counts keep them apart; a bare zero collapses them, which is the
    /// same defect as an omitted year read as « toutes les années ».
    /// </summary>
    public bool NothingWasPublished => CohortsUnpublished == 0 && CohortsSkippedUnderway == 0;

    public const int MaxReportedSkipped = 5;
}

/// <param name="Label">The cohorte's label, so the operator can find it without an id.</param>
public sealed record SkippedCohort(
    int CohortId, string Label, int Periods, int Started, int Evaluations, int AttendanceDays);
