using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Todos.Delete;

public sealed record DeleteTodoCommand(Guid TodoItemId) : ICommand;
