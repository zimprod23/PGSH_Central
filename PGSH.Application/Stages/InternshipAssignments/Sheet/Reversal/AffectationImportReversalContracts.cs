using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;

/// <summary>One import, as a list row — enough to recognise which upload to walk back.</summary>
public sealed record AffectationImportSummary(
    Guid Id,
    string YearLabel,
    string LevelLabel,
    string? FileName,
    DateTime AppliedAtUtc,
    string? AppliedByName,
    AffectationImportStatus Status,
    DateTime? ReversedAtUtc,
    int AffectationCount,
    int ReplacedPeriodCount,
    /// <summary>
    /// Whether « Annuler » is worth offering at all. ⚠ False on an import already reversed — and the
    /// row still says when, because « rien à faire » and « déjà défait » are not the same answer.
    /// </summary>
    bool CanBeReversed);

/// <summary>What undoing one entry would do — or why it cannot.</summary>
public enum AffectationImportReversalRowStatus
{
    /// <summary>The import created the affectation; undoing removes it whole.</summary>
    WillRemoveAffectation,

    /// <summary>The import replaced a rotation; undoing writes the recorded périodes back.</summary>
    WillRestorePeriods,

    /// <summary>
    /// The affectation is no longer there — somebody deleted the student, or a later act removed it.
    /// Skipped, and counted: the import is still reversible, this line simply has nothing left to do.
    /// </summary>
    AlreadyGone,

    // ---- refusals: one of these anywhere refuses the whole reversal ----

    /// <summary>
    /// It has been evaluated since the import. ⚠ The import itself refused to touch a mark, so this one
    /// arrived afterwards — which means the affectation is no longer the one the import wrote, and
    /// undoing would destroy a note this record cannot put back.
    /// </summary>
    EvaluatedSince,

    /// <summary>Attendance was keyed in since the import, and the same reasoning applies.</summary>
    AttendedSince,

    /// <summary>
    /// Its périodes no longer look like what the import wrote — it has been re-planned, transferred or
    /// re-imported since. Undoing would not restore a previous state, it would impose an old one.
    /// </summary>
    ChangedSince,
}

public static class AffectationImportReversalRowStatusExtensions
{
    public static bool IsError(this AffectationImportReversalRowStatus status) =>
        status is not (AffectationImportReversalRowStatus.WillRemoveAffectation
                    or AffectationImportReversalRowStatus.WillRestorePeriods
                    or AffectationImportReversalRowStatus.AlreadyGone);

    public static bool NeedsAttention(this AffectationImportReversalRowStatus status) =>
        status.IsError() || status is AffectationImportReversalRowStatus.AlreadyGone;
}

public sealed record AffectationImportReversalRow(
    Guid RegistrationId,
    string StudentFullName,
    string? Appogee,
    string StageName,
    AffectationImportReversalRowStatus Status,
    int PeriodsToRestore,
    string Message);

/// <summary>
/// The dry run, and — after the act — the record of what was undone. The same shape both times,
/// because it is the same plan.
/// </summary>
/// <param name="AffectationsToRemove">
/// Affectations the import created and the undo deletes whole. ⚠ This is the destructive half, and it
/// is what <c>ConfirmedCount</c> confirms: nothing puts an affectation back except another import.
/// </param>
/// <param name="PeriodsToRestore">
/// Périodes written back onto affectations the import had rebuilt — including their grid cell, their
/// lifecycle flags and, where it applies, their délocalisation motif.
/// </param>
/// <param name="PublishedPeriodsToRestore">
/// How many of those come back linked to a planning cell. ⚠ Stated apart because it is the number that
/// says the promotion's grid and its execution records will agree again — the thing the import broke.
/// </param>
public sealed record AffectationImportReversalReport(
    Guid ImportId,
    string YearLabel,
    string LevelLabel,
    DateTime AppliedAtUtc,
    int Entries,
    int AffectationsToRemove,
    int AffectationsToRestore,
    int PeriodsToRestore,
    int PublishedPeriodsToRestore,
    int PeriodsToRemove,
    int AlreadyGone,
    int ErrorCount,
    bool CanApply,
    IReadOnlyList<AffectationImportReversalRow> Rows,
    bool RowsTruncated,
    IReadOnlyList<string> Notes);
