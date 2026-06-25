using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed record StudentCohortTransferredDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    int  PreviousCohortId,
    int  NewCohortId,
    string? Reason,
    TransferType Type) : IDomainEvent;
