using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Students.Registrations.Create;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Registrations;

public sealed class Create: IEndpoint
{
    public sealed record Request(
        Guid StudentId,
        DateOnly AcademicYear,
        int LevelId,
        string Status);
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/registrations", async (
           Request request,
           ISender sender,
           CancellationToken cancellationToken) =>
        {
            var command = new CreateRegistrationCommand(
                StudentId: request.StudentId,
                AcademicYear: request.AcademicYear,
                LevelId: request.LevelId,
                Status: request.Status
                );
            var result = await sender.Send(command, cancellationToken);
            return result.Match(id => Results.Created($"/registrations/{id}", id), CustomResults.Problem);
            //return Results.Created($"/registrations/{registrationId}", registrationId);
        })
       .WithName("CreateRegistration")
       .WithTags(Tags.Registrations);
    }
}
