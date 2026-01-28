using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Delete;


namespace PGSH.API.Endpoints.Students
{
    public sealed class Delete : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("students/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                var command = new DeleteStudentCommand(id);
                var result = await sender.Send(command, ct);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags(Tags.Students);
        }
    }
}
