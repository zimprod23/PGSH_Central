using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Todos.Get;

public sealed record GetTodosQuery(Guid UserId) : IQuery<List<TodoResponse>>;
