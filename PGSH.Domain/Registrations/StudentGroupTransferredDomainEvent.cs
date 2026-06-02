using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

public sealed record StudentGroupTransferredDomainEvent(
    Guid    RegistrationId,
    Guid    StudentId,
    int?    FromGroupId,
    int     ToGroupId,
    string? Reason) : IDomainEvent;
