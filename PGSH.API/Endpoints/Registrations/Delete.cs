using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.Delete;

namespace PGSH.API.Endpoints.Registrations;

public sealed class Delete : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // We pass the studentId in the body or as a query param to verify ownership
        app.MapDelete("/registrations/{id:guid}", async (
            Guid id,
            Guid studentId, // Received from query string or header
            ISender sender,
            CancellationToken ct) =>
        {
            var command = new DeleteRegistrationCommand(id, studentId);

            var result = await sender.Send(command, ct);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithName("DeleteRegistration")
        .WithTags(Tags.Registrations);
    }
}