using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed record AssignmentValidatedDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    decimal? FinalScore) : IDomainEvent;
