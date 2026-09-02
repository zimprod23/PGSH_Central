using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.ServicePeriods;

public sealed record ServicePeriodResponse(
    Guid Id,
    Guid InternshipAssignmentId,
    string StudentFullName,
    string? StudentCne,
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
    string? PauseReason = null,
    bool IsInterrupted = false,
    // Where the rotation stands, decided once by ServicePeriodLifecycle. ⚠ Sent rather than left to
    // the client to infer from IsComplete/HasEvaluation/IsStarted: the frontend was re-deriving the
    // same four-way split in TypeScript, which is the rule stated twice across a network boundary
    // with nothing able to catch them disagreeing. In particular Planned — published but not opened
    // — is the state that was invisible, and it is not something a client should have to work out.
    ServicePeriodState State = ServicePeriodState.Underway);

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
