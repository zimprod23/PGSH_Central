using PGSH.SharedKernel;

namespace PGSH.Domain.Todos;

public sealed record TodoItemCompletedDomainEvent(Guid TodoItemId) : IDomainEvent;
