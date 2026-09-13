using PGSH.Domain.Registrations;
using PGSH.SharedKernel;
using AppResult = PGSH.SharedKernel.Result;

namespace PGSH.Domain.Stages;

/// <summary>Where one import stands.</summary>
public enum AffectationImportStatus
{
    /// <summary>Written, and still standing.</summary>
    Applied,

    /// <summary>Walked back. ⚠ Kept rather than deleted — see <see cref="AffectationImport"/>.</summary>
    Reversed,
}

/// <summary>What one line of the canvas did to one affectation.</summary>
public enum AffectationImportOutcome
{
    /// <summary>The affectation did not exist. Undoing it removes the affectation entirely.</summary>
    Created,

    /// <summary>It existed and was rebuilt. Undoing it puts the recorded périodes back.</summary>
    Rebuilt,

    /// <summary>It was replaced by a single external période. Undone like <see cref="Rebuilt"/>.</summary>
    Delocalized,
}

/// <summary>
/// One application of the canevas des affectations, recorded so it can be walked back.
///
/// <para><b>Why this is an aggregate and not a marker column on <see cref="ServicePeriod"/>.</b> The
/// question the faculty asks three months later is not « which rows came from a spreadsheet » — it is
/// « undo what I did on Thursday ». That has an identity, a scope, an author, a moment, and a state
/// (standing or walked back); and it needs to know what it <i>destroyed</i>, not merely what it wrote,
/// or its undo restores nothing and leaves the student worse off than before the import. A nullable
/// <c>Guid</c> on a période can carry none of that.</para>
/// </summary>
/// <remarks>
/// <para>⚠ <b>The undo is total, and it is the import's refusals that make it so.</b> A line whose
/// affectation carries a mark is refused (<c>AlreadyMarked</c>), and so is one whose périodes carry
/// attendance (<c>AlreadyAttended</c>). So an import never destroys anything that cannot be written
/// back from this record: a période is a service, a window and four flags, and those are recorded in
/// <see cref="ReplacedPeriod"/>. Were either refusal relaxed, this class would start lying — an undo
/// that silently restores less than it removed is worse than one that refuses.</para>
///
/// <para>⚠ <b>A reversed import is kept, not deleted.</b> « Cet étudiant a-t-il été planifié par un
/// fichier, puis dé-planifié ? » is a question the dossier must be able to answer, and a row that
/// deletes itself on undo answers « il ne s'est rien passé ». Same reason a released
/// <c>RegistrationHold</c> survives its release.</para>
///
/// <para>⚠ <b>It records the plan, not the file.</b> Storing the uploaded workbook would put a
/// promotion's names and identifiers in a second place, under no retention rule, for a convenience
/// nobody asked for. What is kept is what the act did.</para>
/// </remarks>
public sealed class AffectationImport : Entity
{
    public Guid Id { get; set; }

    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = default!;

    public int LevelId { get; set; }

    /// <summary>The file's own name, as the operator's machine had it. Free text, for recognition.</summary>
    public string? FileName { get; set; }

    public DateTime AppliedAtUtc { get; private set; }
    public Guid? AppliedByUserId { get; private set; }

    public AffectationImportStatus Status { get; private set; } = AffectationImportStatus.Applied;

    public DateTime? ReversedAtUtc { get; private set; }
    public Guid? ReversedByUserId { get; private set; }

    public ICollection<AffectationImportEntry> Entries { get; set; } = new List<AffectationImportEntry>();

    /// <summary>How many affectations this import wrote. Stored rather than counted from
    /// <see cref="Entries"/>, which a caller may not have loaded.</summary>
    public int AffectationCount { get; private set; }

    /// <summary>How many périodes it destroyed to make room. The number the operator confirmed.</summary>
    public int ReplacedPeriodCount { get; private set; }

    private AffectationImport() { }

    public static AffectationImport Record(
        int academicYearId, int levelId, string? fileName, DateTime onUtc, Guid? byUserId) =>
        new()
        {
            AcademicYearId  = academicYearId,
            LevelId         = levelId,
            FileName        = fileName,
            AppliedAtUtc    = onUtc,
            AppliedByUserId = byUserId,
        };

    /// <summary>
    /// Records what one line did. ⚠ <paramref name="replaced"/> is what was <b>destroyed</b>, which is
    /// the half an undo needs; what was written is on the affectation itself.
    /// </summary>
    public void Wrote(
        Guid registrationId,
        int stageId,
        Guid internshipAssignmentId,
        AffectationImportOutcome outcome,
        int writtenPeriods,
        IReadOnlyList<ReplacedPeriod> replaced)
    {
        // No pre-set Id on the children: this graph is added whole, but the aggregate is also
        // completed entry by entry while it is already tracked. Same gotcha as everywhere else.
        var entry = new AffectationImportEntry
        {
            RegistrationId         = registrationId,
            StageId                = stageId,
            InternshipAssignmentId = internshipAssignmentId,
            Outcome                = outcome,
            WrittenPeriodCount     = writtenPeriods,
        };

        foreach (var period in replaced)
            entry.ReplacedPeriods.Add(period);

        Entries.Add(entry);
        AffectationCount++;
        ReplacedPeriodCount += replaced.Count;
    }

    /// <summary>
    /// Marks the import walked back. The rows themselves are restored by the caller, through
    /// <see cref="InternshipAssignment.RestoreRotation"/> and by removing what was created — this
    /// records that it happened, and refuses a second time.
    /// </summary>
    public Result Reverse(DateTime onUtc, Guid? byUserId)
    {
        if (Status == AffectationImportStatus.Reversed)
            return AppResult.Failure(StageErrors.AffectationImportAlreadyReversed(Id));

        Status           = AffectationImportStatus.Reversed;
        ReversedAtUtc    = onUtc;
        ReversedByUserId = byUserId;

        Raise(new AffectationImportReversedDomainEvent(Id, AcademicYearId, LevelId, AffectationCount));
        return AppResult.Success();
    }
}

/// <summary>What one import did to one (student, stage).</summary>
public sealed class AffectationImportEntry
{
    public Guid Id { get; set; }

    public Guid AffectationImportId { get; set; }
    public AffectationImport AffectationImport { get; set; } = default!;

    public Guid RegistrationId { get; set; }
    public int StageId { get; set; }

    /// <summary>
    /// The affectation written. ⚠ <b>Deliberately not a foreign key.</b> Undoing a <c>Created</c> entry
    /// deletes that affectation, and a FK would either take this record with it (losing the trace the
    /// class exists to keep) or refuse the delete. The id is kept as a plain value, and a reversal
    /// that cannot find it reports « déjà supprimée » rather than failing.
    /// </summary>
    public Guid InternshipAssignmentId { get; set; }

    public AffectationImportOutcome Outcome { get; set; }

    /// <summary>
    /// How many périodes the import <b>wrote</b> onto this affectation.
    ///
    /// <para>⚠ <b>Recorded, because the undo has to tell « still what I left » from « changed
    /// since ».</b> Counting the affectation's périodes at reversal time answers what is there now,
    /// which is trivially equal to itself; the question is whether it still equals what the import
    /// left. Without this number the check passes on an affectation somebody has re-planned, and the
    /// undo writes an old state over a newer one.</para>
    /// </summary>
    public int WrittenPeriodCount { get; set; }

    /// <summary>
    /// The périodes this line destroyed, exactly as they stood. Empty on <see cref="AffectationImportOutcome.Created"/>.
    /// </summary>
    public ICollection<ReplacedPeriod> ReplacedPeriods { get; set; } = new List<ReplacedPeriod>();
}

/// <summary>
/// A période as it stood before an import replaced it — everything needed to write it back.
/// </summary>
/// <remarks>
/// ⚠ <b><see cref="CohortSlotAssignmentId"/> is part of the record, and that is the point.</b> An
/// import may overwrite a <i>published</i> rotation; restoring the périodes without their cell would
/// put the student back in the right service on the right dates and leave the plan and the execution
/// records permanently out of agreement — the grid still showing a cell nothing was published from.
/// The link is what makes the undo put the promotion back where it was.
///
/// <para>⚠ No évaluation and no attendance are recorded here, and none needs to be: the import refuses
/// to touch a période carrying either. This class is only as truthful as those two guards.</para>
/// </remarks>
public sealed class ReplacedPeriod
{
    public Guid Id { get; set; }

    public Guid AffectationImportEntryId { get; set; }
    public AffectationImportEntry AffectationImportEntry { get; set; } = default!;

    public int ServiceId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public bool IsStarted { get; set; }
    public bool IsComplete { get; set; }
    public bool IsInterrupted { get; set; }
    public bool IsPaused { get; set; }
    public bool IsDelocalized { get; set; }

    /// <summary>The grid cell it was published from, when it was. Null for an ad-hoc période.</summary>
    public int? CohortSlotAssignmentId { get; set; }

    /// <summary>The motif, when the replaced période was itself a délocalisation.</summary>
    public string? DelocalizationReason { get; set; }
}
