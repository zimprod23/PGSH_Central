using PGSH.Application.Students.GetMany;
using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;


namespace PGSH.API.Endpoints.Students
{
    public sealed class GetMany : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("students", async ([AsParameters] GetStudentsQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.Students);
        }
    }
}
