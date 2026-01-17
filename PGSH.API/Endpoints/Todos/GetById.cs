
using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Todos.GetById;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Todos
{
    public sealed class GetById : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("todos/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var query = new GetTodoByIdQuery(id);
                Result<TodoResponse> result = await sender.Send(query, cancellationToken);
                return result.Match(Results.Ok,CustomResults.Problem);
            })
            .WithTags(Tags.Todos)
            .RequireAuthorization();
        }
    }
}
