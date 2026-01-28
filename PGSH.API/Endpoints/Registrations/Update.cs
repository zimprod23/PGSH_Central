
using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Registrations.Update;

namespace PGSH.API.Endpoints.Registrations;

public sealed class Update : IEndpoint
{
    public sealed record Request(
        Guid StudentId,
        DateOnly AcademicYear,
        int LevelId,
        string Status,
        string? FailureDescription = null,
        List<string>? FailureNotes = null, // Matches the Command type
        bool? Cheat = null);
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/registrations/{id:guid}", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken ct) =>
        {
            var command = new UpdateRegistrationCommand(
                RegistrationId: id,
                StudentId: request.StudentId,
                Status: request.Status,
                AcademicYear: request.AcademicYear,
                LevelId: request.LevelId,
                FailureDescription: request.FailureDescription,
                FailureNotes: request.FailureNotes,
                Cheat: request.Cheat);

            var result = await sender.Send(command, ct);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithName("UpdateRegistration")
        .WithTags(Tags.Registrations);
    }
}
