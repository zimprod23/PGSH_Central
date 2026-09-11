using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.UnpublishSchedule;

/// <param name="Force">
/// Proceed even though the rotation has begun. Unpublishing deletes the <c>ServicePeriod</c>s, and
/// evaluations, attendance, pauses and délocalisations cascade from them — so once anything has
/// started this stops being the inverse of publishing and becomes the destruction of what happened.
/// The caller has to say so, having been told the numbers.
/// </param>
public sealed record UnpublishCohortScheduleCommand(int CohortId, bool Force = false)
    : ICommand<UnpublishResult>, IAuditableCommand
{
    public string AuditAction => "COHORT_SCHEDULE_UNPUBLISHED";
    public string AuditEntityType => "Cohort";
    public string? AuditEntityId => CohortId.ToString();

    /// <summary>
    /// ⚠ <c>Force</c> voyage dans l'entrée, et c'est le champ qui compte : forcé, cet acte détruit
    /// des notes de chef et des journées de présence. Une dépublication ordinaire et un passage en
    /// force ne doivent pas se lire pareil dans le registre.
    /// </summary>
    public string? AuditMetadata => AuditMetadataJson.Of(("forced", Force));
}

/// <param name="PeriodsRemoved">Grid-linked periods deleted.</param>
/// <param name="EvaluationsLost">Evaluations that went with them — 0 on the ordinary path.</param>
/// <param name="AttendanceDaysLost">Attendance rows that went with them.</param>
/// <param name="AdHocPeriodsKept">
/// Periods left untouched because no cell produced them: imported history, délocalisations,
/// revalidations. Reported so the caller can see that unpublishing did not reach into them.
/// </param>
public sealed record UnpublishResult(
    int PeriodsRemoved, int EvaluationsLost, int AttendanceDaysLost, int AdHocPeriodsKept);
