using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

public sealed record RegistrationUpdatedDomainEvent(Guid RegistrationId, string NewStatus) : IDomainEvent;