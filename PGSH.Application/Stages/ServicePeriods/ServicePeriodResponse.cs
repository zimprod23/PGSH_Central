namespace PGSH.Application.Stages.ServicePeriods;

public sealed record ServicePeriodResponse(
    Guid Id,
    Guid InternshipAssignmentId,
    string StudentFullName,
    string StudentCne,
    string StudentAppogee,
    int ServiceId,
    string ServiceName,
    string HospitalName,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsComplete,
    bool HasEvaluation,
    string AcademicGroupLabel,
    string StageName,
    string? LevelLabel,
    TransferMarker? Transfer = null,
    bool IsPaused = false,
    string? PauseReason = null);

/// <summary>
/// Overlay marking a worklist row that no longer matches the chef's live roster because
/// of a group transfer. <see cref="TransferDirection.Outgoing"/> rows are real, published
/// periods whose student has since moved away (shown grayed, "→ {GroupLabel} · {ServiceName}");
/// <see cref="TransferDirection.Incoming"/> rows are synthesized for students who transferred
/// into the chef's service but whose periods were never re-published (shown green,
/// "← {GroupLabel} · {ServiceName}"). Both are informational and not actionable.
/// </summary>
public sealed record TransferMarker(
    TransferDirection Direction,
    string GroupLabel,
    string? ServiceName,
    string? Reason,
    DateOnly? Date);

public enum TransferDirection
{
    Outgoing,
    Incoming,
}
