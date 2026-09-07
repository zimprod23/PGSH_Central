using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Delocalization.Bulk;

// Who goes is StudentTargets, in Application/Students/Selection — shared with the nominative roster
// assignment, which names its students exactly the same way. It lived here first.

/// <summary>What the bulk act would do to one student, in the words the preview shows.</summary>
public enum BulkDelocalizationRowStatus
{
    /// <summary>Nothing to lose: the stage is planned, or has no rotation at all yet.</summary>
    WillDelocalize,

    /// <summary>Already served outside; the ad-hoc period is replaced with the dates and motif of this act.</summary>
    WillReplace,

    /// <summary>
    /// Applied, but it deletes rotations that had begun. Legitimate — the student left — and still
    /// worth naming: it is movement the app recorded, and an operator who is not told assumes the
    /// row cost nothing.
    /// </summary>
    WillDropUnderway,

    /// <summary>Refused: a mark is on record and a délocalisation would erase it.</summary>
    AlreadyMarked,

    /// <summary>Refused: the student is in no roster this year, so there is no cohorte to délocaliser from.</summary>
    NoRoster,

    /// <summary>Refused: this stage has no cohorte for the student's roster — usually the wrong promotion.</summary>
    NoCohort,

    /// <summary>Refused: the identifier matches nobody registered this year.</summary>
    NotFound,

    /// <summary>Refused: the student is registered, but in another year than the one being planned.</summary>
    WrongYear,
}

public static class BulkDelocalizationRowStatusExtensions
{
    /// <summary>Whether the row is written. The others are reported and skipped — never silently.</summary>
    public static bool IsApplicable(this BulkDelocalizationRowStatus status) =>
        status is BulkDelocalizationRowStatus.WillDelocalize
               or BulkDelocalizationRowStatus.WillReplace
               or BulkDelocalizationRowStatus.WillDropUnderway;
}

/// <summary>One student's line on the preview, and the same line on the report after the apply.</summary>
public sealed record BulkDelocalizationRow(
    Guid?  RegistrationId,
    string StudentName,
    string? Cne,
    string? Appogee,
    string? GroupLabel,
    BulkDelocalizationRowStatus Status,
    string Message,
    /// <summary>The identifier that produced this row, when it came from a pasted list — so an
    /// unmatched line can be found in the file it was typed in.</summary>
    string? SourceIdentifier = null);

/// <summary>
/// What the act would do, or did. The preview and the apply return the same shape because they run
/// the same planner and nothing else — a preview computed by different code is a preview of nothing.
/// </summary>
/// <remarks>
/// ⚠ <b><paramref name="Rows"/> is capped, and every count here is measured before the cap.</b> A
/// selection is a whole promotion when the operator asks for one — 933 students on the 3ᵉ MED — and a
/// single-object response carrying one row each is exactly the shape that shipped 4 725 students in
/// one object and took the browser down. The counts are therefore <b>stored</b>, not derived from
/// what survived: a number computed off a capped list is a number that silently reads low.
/// </remarks>
/// <param name="Rows">
/// The lines to show, <b>refusals first</b>. Capped at <see cref="RowCap"/>: it is the refusals the
/// operator has to act on, so they are the ones that must never be the rows dropped.
/// </param>
/// <param name="TotalRowCount">How many lines the selection produced in all, cap or no cap.</param>
public sealed record BulkDelocalizationReport(
    int      StageId,
    string   StageName,
    int      ServiceId,
    string   ServiceName,
    bool     ServiceIsExternal,
    int      AcademicYearId,
    string   AcademicYearLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<BulkDelocalizationRow> Rows,
    int      TotalRowCount,
    /// <summary>How many students would actually be written — the number the operator confirms.</summary>
    int      ApplicableCount,
    int      RefusedCount,
    /// <summary>Of the applicable rows, how many delete a rotation that had begun.</summary>
    int      UnderwayCount,
    /// <summary>Of the applicable rows, how many were already délocalisés and are being replaced.</summary>
    int      ReplacedCount)
{
    /// <summary>
    /// How many lines travel with the report. Large enough that an ordinary act — a roster, a dozen
    /// names — is shown whole, small enough that a whole promotion is not.
    /// </summary>
    public const int RowCap = 200;

    /// <summary>
    /// ⚠ Said out loud rather than left to be inferred from an empty list. Nobody selected, every
    /// student already marked and « the group is not in this year » all produce zero rows, and they
    /// call for three different acts.
    /// </summary>
    public bool IsEmpty => TotalRowCount == 0;

    /// <summary>True when the list on screen is not the whole story, so the screen can say so.</summary>
    public bool RowsTruncated => TotalRowCount > Rows.Count;

    /// <summary>
    /// Builds the report from every row the plan produced, counting first and cutting after.
    /// </summary>
    public static BulkDelocalizationReport From(
        int stageId, string stageName, int serviceId, string serviceName, bool serviceIsExternal,
        int academicYearId, string academicYearLabel, DateOnly startDate, DateOnly endDate,
        IReadOnlyList<BulkDelocalizationRow> allRows) =>
        new(stageId, stageName, serviceId, serviceName, serviceIsExternal,
            academicYearId, academicYearLabel, startDate, endDate,
            // Refusals first, so the cap can only ever drop lines that need no decision.
            allRows.OrderBy(r => r.Status.IsApplicable() ? 1 : 0).Take(RowCap).ToList(),
            allRows.Count,
            allRows.Count(r => r.Status.IsApplicable()),
            allRows.Count(r => !r.Status.IsApplicable()),
            allRows.Count(r => r.Status == BulkDelocalizationRowStatus.WillDropUnderway),
            allRows.Count(r => r.Status == BulkDelocalizationRowStatus.WillReplace));
}

/// <summary>
/// A row the plan will execute, paired with the aggregate it acts on. The apply walks these; the
/// preview throws them away.
/// </summary>
/// <remarks>
/// ⚠ <b>Tracked, and deliberately not <c>AsNoTracking</c>.</b> These are the objects the apply
/// mutates. A shared query marked no-tracking makes its host no-tracking too, which is how a bulk
/// apply came to report success while <c>SaveChanges</c> wrote nothing.
/// </remarks>
internal sealed record PlannedDelocalization(
    Guid RegistrationId,
    int  CohortId,
    InternshipAssignment? Assignment);

internal sealed record BulkDelocalizationPlan(
    BulkDelocalizationReport Report,
    IReadOnlyList<PlannedDelocalization> Work,
    int StageId,
    int ServiceId,
    DateOnly StartDate,
    DateOnly EndDate);
