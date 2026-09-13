using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// A période being written back exactly as it stood before an import replaced it — what
/// <see cref="InternshipAssignment.RestoreRotation"/> is handed.
/// </summary>
/// <remarks>
/// ⚠ <b>Richer than <see cref="DeclaredPeriod"/>, and deliberately so.</b> A human declaring a
/// rotation may state only a service and a window — the lifecycle is not theirs to set, or a
/// spreadsheet could hand the faculty a stage already « terminé » and therefore evaluable. An undo is
/// the opposite act: it is not declaring anything, it is putting back something the system itself
/// recorded, so it carries the flags and the grid cell or it is not a restoration at all.
/// </remarks>
public readonly record struct RestoredPeriod(
    int ServiceId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsStarted,
    bool IsComplete,
    bool IsInterrupted,
    bool IsPaused,
    bool IsDelocalized,
    int? CohortSlotAssignmentId,
    string? DelocalizationReason)
{
    public static RestoredPeriod From(ReplacedPeriod replaced) => new(
        replaced.ServiceId,
        replaced.StartDate,
        replaced.EndDate,
        replaced.IsStarted,
        replaced.IsComplete,
        replaced.IsInterrupted,
        replaced.IsPaused,
        replaced.IsDelocalized,
        replaced.CohortSlotAssignmentId,
        replaced.DelocalizationReason);
}

/// <summary>An import was walked back, as a whole.</summary>
public sealed record AffectationImportReversedDomainEvent(
    Guid AffectationImportId,
    int AcademicYearId,
    int LevelId,
    int AffectationCount) : IDomainEvent;

/// <summary>
/// One student's rotation was put back as it stood before an import.
/// </summary>
/// <param name="RestoredPeriods">What came back. Zero means the affectation itself was removed —
/// the import had created it, so there was nothing before it to restore.</param>
public sealed record AffectationImportRolledBackDomainEvent(
    Guid InternshipAssignmentId,
    Guid RegistrationId,
    int StageId,
    int RestoredPeriods,
    int RemovedPeriods) : IDomainEvent;
