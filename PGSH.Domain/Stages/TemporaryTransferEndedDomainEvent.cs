using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed record TemporaryTransferEndedDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    int  TransferCohortId,
    int  OriginalCohortId,
    string? Reason) : IDomainEvent;
