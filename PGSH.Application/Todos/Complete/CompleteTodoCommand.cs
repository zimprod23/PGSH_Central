using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Todos.Complete;

public sealed record CompleteTodoCommand(Guid TodoItemId) : ICommand;
