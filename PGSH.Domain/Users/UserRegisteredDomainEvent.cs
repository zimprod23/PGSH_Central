using PGSH.SharedKernel;

namespace PGSH.Domain.Users;

public sealed record UserRegisteredDomainEvent(Guid UserId) : IDomainEvent;
