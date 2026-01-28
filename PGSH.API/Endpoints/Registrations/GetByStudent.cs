using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.GetByStudent;

namespace PGSH.API.Endpoints.Registrations;

public sealed class GetByStudent : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Notice the route: it defines the registrations as belonging to a student
        app.MapGet("students/{studentId:guid}/registrations", async (
            Guid studentId,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new GetStudentRegistrationsQuery(studentId);

            var result = await sender.Send(query, ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetStudentRegistrations")
        .WithTags(Tags.Registrations); // Keep grouped under Registrations in Swagger
    }
}