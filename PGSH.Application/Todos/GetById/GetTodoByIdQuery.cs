using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Todos.GetById;

public sealed record GetTodoByIdQuery(Guid TodoItemId) : IQuery<TodoResponse>;
