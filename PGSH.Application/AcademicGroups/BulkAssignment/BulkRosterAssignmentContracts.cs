namespace PGSH.Application.AcademicGroups.BulkAssignment;

/// <summary>What the act would do to one student, in the words the preview shows.</summary>
public enum BulkRosterAssignmentRowStatus
{
    /// <summary>
    /// The registration is in no roster yet, so it is attached and its affectations created — the
    /// bulk form of « affecter à un groupe ».
    /// </summary>
    WillJoin,

    /// <summary>
    /// The registration is in another roster and nothing has happened yet, so it moves — the bulk
    /// form of « changement de groupe », which leaves no trace: afterwards the file reads exactly as
    /// if the répartition had put the student here from the start.
    /// </summary>
    WillMove,

    /// <summary>Nothing to do: already in the target roster. Reported rather than counted as work.</summary>
    AlreadyThere,

    /// <summary>
    /// Refused: rotations have begun, been marked, or carry attendance. « Il n'y a jamais été » stops
    /// being a correction there, and the act that carries a running rotation across — and keeps the
    /// trace, precisely because there is now something to trace — is <c>TransferStudentCommand</c>.
    /// </summary>
    Underway,

    /// <summary>
    /// Refused: the target roster has no cohorte for a stage the student is already affected to, so
    /// his affectation would have nowhere to land.
    /// </summary>
    TargetMissingStage,

    /// <summary>
    /// Refused: a roster is keyed (année, niveau, numéro), so a 4ᵉ année cannot join a 5ᵉ année
    /// roster. ⚠ Named separately from <see cref="WrongYear"/> — a student on the wrong promotion's
    /// list and a student on the wrong year's are two different corrections.
    /// </summary>
    WrongPromotion,

    /// <summary>Refused: the cursus is over (diplômé, exclu), so there is nothing left to plan.</summary>
    CursusEnded,

    /// <summary>Refused: no student carries this identifier, or no such registration exists.</summary>
    NotFound,

    /// <summary>Refused: the student is known, but not through a registration of the year asked for.</summary>
    WrongYear,
}

public static class BulkRosterAssignmentRowStatusExtensions
{
    /// <summary>
    /// Whether the row is written. ⚠ <see cref="BulkRosterAssignmentRowStatus.AlreadyThere"/> is
    /// deliberately <b>not</b> applicable: it is not a refusal — nobody has to act on it — but
    /// counting it as work would inflate the number the operator confirms, and that number is the
    /// only guard this act has.
    /// </summary>
    public static bool IsApplicable(this BulkRosterAssignmentRowStatus status) =>
        status is BulkRosterAssignmentRowStatus.WillJoin
               or BulkRosterAssignmentRowStatus.WillMove;

    /// <summary>
    /// Whether the row needs a human decision. Separates a refusal from « rien à faire », so the
    /// report can put the first at the top and never let the cap drop one.
    /// </summary>
    public static bool NeedsAttention(this BulkRosterAssignmentRowStatus status) =>
        !status.IsApplicable() && status != BulkRosterAssignmentRowStatus.AlreadyThere;
}

/// <summary>One student's line on the preview, and the same line on the report after the apply.</summary>
public sealed record BulkRosterAssignmentRow(
    Guid?   RegistrationId,
    string  StudentName,
    string? Cne,
    string? Appogee,
    /// <summary>The roster the student is in today — null when he is in none.</summary>
    string? CurrentGroupLabel,
    BulkRosterAssignmentRowStatus Status,
    string  Message,
    /// <summary>The identifier that produced this row, when it came from a pasted list — so an
    /// unmatched line can be found in the file it was typed in.</summary>
    string? SourceIdentifier = null);

/// <summary>
/// What the act would do, or did. The preview and the apply return the same shape because they run
/// the same planner and nothing else — a preview computed by different code is a preview of nothing.
/// </summary>
/// <remarks>
/// ⚠ <b><paramref name="Rows"/> is capped, and every count here is measured before the cap.</b> A
/// selection can be a whole promotion, and a single-object response carrying one row each is exactly
/// the shape that shipped 4 725 students in one object and took the browser down. The counts are
/// therefore <b>stored</b>, not derived from what survived: a number computed off a capped list is a
/// number that silently reads low.
/// </remarks>
/// <param name="Rows">
/// The lines to show, <b>refusals first</b>. Capped at <see cref="RowCap"/>: it is the refusals the
/// operator has to act on, so they are the ones that must never be the rows dropped.
/// </param>
/// <param name="ApplicableCount">
/// The students the act would actually write — the number sent back as <c>ConfirmedCount</c>.
/// </param>
/// <param name="JoinCount">Of those, the ones attached from no roster at all.</param>
/// <param name="MoveCount">Of those, the ones moved out of another roster.</param>
/// <param name="AlreadyThereCount">
/// Already in the target roster. ⚠ Its own number rather than folded into the refusals: re-sending a
/// corrected list is the normal way this act is used, so most of a second run lands here and reading
/// that as « 60 refus » would look like the run failed.
/// </param>
public sealed record BulkRosterAssignmentReport(
    int    TargetGroupId,
    string TargetGroupLabel,
    int    AcademicYearId,
    string AcademicYearLabel,
    IReadOnlyList<BulkRosterAssignmentRow> Rows,
    int    TotalRowCount,
    int    ApplicableCount,
    int    RefusedCount,
    int    JoinCount,
    int    MoveCount,
    int    AlreadyThereCount)
{
    /// <summary>
    /// How many lines travel with the report. Large enough that an ordinary act — a roster, a dozen
    /// names — is shown whole, small enough that a whole promotion is not.
    /// </summary>
    public const int RowCap = 200;

    /// <summary>
    /// ⚠ Said out loud rather than left to be inferred from an empty list. Nobody selected, every
    /// student already in the roster and « the list names another promotion » all produce zero
    /// applicable rows, and they call for three different acts.
    /// </summary>
    public bool IsEmpty => TotalRowCount == 0;

    /// <summary>True when the list on screen is not the whole story, so the screen can say so.</summary>
    public bool RowsTruncated => TotalRowCount > Rows.Count;

    /// <summary>
    /// Builds the report from every row the plan produced, counting first and cutting after.
    /// </summary>
    public static BulkRosterAssignmentReport From(
        int targetGroupId, string targetGroupLabel,
        int academicYearId, string academicYearLabel,
        IReadOnlyList<BulkRosterAssignmentRow> allRows) =>
        new(targetGroupId, targetGroupLabel, academicYearId, academicYearLabel,
            // Refusals first, then « rien à faire », then the work: the cap can only ever drop lines
            // nobody has to decide anything about.
            [.. allRows
                .OrderBy(r => r.Status.NeedsAttention() ? 0 : r.Status.IsApplicable() ? 1 : 2)
                .Take(RowCap)],
            allRows.Count,
            allRows.Count(r => r.Status.IsApplicable()),
            allRows.Count(r => r.Status.NeedsAttention()),
            allRows.Count(r => r.Status == BulkRosterAssignmentRowStatus.WillJoin),
            allRows.Count(r => r.Status == BulkRosterAssignmentRowStatus.WillMove),
            allRows.Count(r => r.Status == BulkRosterAssignmentRowStatus.AlreadyThere));
}

/// <summary>
/// A row the plan will execute. The apply walks these; the preview throws them away.
/// </summary>
/// <param name="Joins">
/// True when the registration is in no roster and has to be attached rather than moved. The two are
/// different acts with different consequences — one creates affectations, the other re-points them —
/// and deciding which by re-reading the registration at apply time would let the answer change
/// between the preview and the write.
/// </param>
internal sealed record PlannedRosterAssignment(Guid RegistrationId, bool Joins);

internal sealed record BulkRosterAssignmentPlan(
    BulkRosterAssignmentReport Report,
    IReadOnlyList<PlannedRosterAssignment> Work,
    int TargetGroupId);
