
using MediatR;
using PGSH.API.Endpoints.Users;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Todos.Delete;
using PGSH.Infrastructure.Authorization;

namespace PGSH.API.Endpoints.Todos
{
    public sealed class Delete : IEndpoint
    {
        [HasPermission(permission:Permissions.UsersAccess)]
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("todos/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new DeleteTodoCommand(id);
                var result = await sender.Send(command, cancellationToken);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags(Tags.Todos)
            .RequireAuthorization();
        }
    }
}
