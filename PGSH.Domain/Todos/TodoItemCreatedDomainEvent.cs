using PGSH.SharedKernel;

namespace PGSH.Domain.Todos;

public sealed record TodoItemCreatedDomainEvent(Guid TodoItemId) : IDomainEvent;
