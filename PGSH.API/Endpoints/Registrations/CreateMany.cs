using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.CreateMany;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Registrations;

public sealed class CreateMany : IEndpoint
{
    public sealed record Request(
        List<Guid> StudentIds,
        int AcademicYearId,
        int LevelId,
        string Status = "Pending");

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/registrations/bulk", async (
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateManyRegistrationsCommand(
                StudentIds: request.StudentIds,
                AcademicYearId: request.AcademicYearId,
                LevelId: request.LevelId,
                Status: request.Status
            );

            var result = await sender.Send(command, cancellationToken);

            // Using Match to handle the outer Result. 
            // If the Command logic itself failed (e.g., Level not found), return Problem.
            // If the Command ran, return 200 OK with the BulkResponse (even if it contains item-level failures).
            return result.Match(
                response => Results.Ok(response),
                CustomResults.Problem);
        })
        .WithName("CreateManyRegistrations")
        .WithTags(Tags.Registrations);
    }
}