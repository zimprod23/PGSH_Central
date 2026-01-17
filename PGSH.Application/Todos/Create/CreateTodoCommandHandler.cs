using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Todos;
using PGSH.Domain.Users;
using Microsoft.EntityFrameworkCore;
using PGSH.SharedKernel;

namespace PGSH.Application.Todos.Create;

internal sealed class CreateTodoCommandHandler(
    IApplicationDbContext context,
    IDateTimeProvider dateTimeProvider,
    IUserContext userContext)
    : ICommandHandler<CreateTodoCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTodoCommand command, CancellationToken cancellationToken)
    {
        if (userContext.UserId != command.UserId)
        {
            return Result.Failure<Guid>(UserErrors.Unauthorized());
        }

        User? user = await context.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<Guid>(UserErrors.NotFound(command.UserId));
        }

        var todoItem = new TodoItem
        {
            UserId = user.Id,
            Description = command.Description,
            Priority = command.Priority,
            DueDate = command.DueDate,
            Labels = command.Labels,
            IsCompleted = false,
            CreatedAt = dateTimeProvider.UtcNow
        };

        todoItem.Raise(new TodoItemCreatedDomainEvent(todoItem.Id));

        context.TodoItems.Add(todoItem);

        await context.SaveChangesAsync(cancellationToken);

        return todoItem.Id;
    }
}
