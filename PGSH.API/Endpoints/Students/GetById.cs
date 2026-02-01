using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.GetById;


namespace PGSH.API.Endpoints.Students
{
    public sealed class GetById : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/students/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                var query = new GetStudentByIdQuery(id);
                var result = await sender.Send(query, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.Students);
        }
    }
}
