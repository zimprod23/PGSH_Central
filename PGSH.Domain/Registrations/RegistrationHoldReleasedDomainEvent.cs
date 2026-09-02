using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// Somebody cleared a hold, so the registration takes part in planning again.
/// </summary>
public sealed record RegistrationHoldReleasedDomainEvent(
    Guid RegistrationId,
    Guid StudentId,
    Guid HoldId,
    RegistrationHoldReason Reason,
    string ReleaseNote) : IDomainEvent;
