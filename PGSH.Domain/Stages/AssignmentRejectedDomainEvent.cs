using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed record AssignmentRejectedDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId) : IDomainEvent;
