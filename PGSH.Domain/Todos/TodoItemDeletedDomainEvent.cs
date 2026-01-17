using PGSH.SharedKernel;

namespace PGSH.Domain.Todos;

public sealed record TodoItemDeletedDomainEvent(Guid TodoItemId) : IDomainEvent;
