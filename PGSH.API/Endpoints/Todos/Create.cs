
using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Todos.Create;
using PGSH.Domain.Todos;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Todos
{
    public sealed class Create : IEndpoint
    {
        public sealed record Request
        {
            //public Guid UserId { get; set; }
            public string Description { get; set; }
            public DateTime? DueDate { get; set; }
            public List<string> Labels { get; set; } = [];
            public int Priority { get; set; }
        };
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("todos", async (Request request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new CreateTodoCommand
                {
                    //UserId = request.UserId,
                    Description = request.Description,
                    DueDate = request.DueDate,
                    Labels = request.Labels,
                    Priority = (Priority)request.Priority
                };
                Result<Guid> result = await sender.Send(command, cancellationToken);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
             .WithTags(Tags.Todos)
             .AllowAnonymous();
             //.RequireAuthorization();
        }
    }
}
