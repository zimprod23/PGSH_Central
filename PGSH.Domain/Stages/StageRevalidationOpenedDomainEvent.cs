using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// A stage the student already failed has been re-opened on a later registration. Carries the
/// registration the retake hangs off plus the one that produced the failure, so the audit trail
/// records which year is being made good.
/// </summary>
public sealed record StageRevalidationOpenedDomainEvent(
    Guid    AssignmentId,
    Guid    RegistrationId,
    Guid    PreviousRegistrationId,
    int     StageId,
    int     CohortId,
    string? Reason) : IDomainEvent;
