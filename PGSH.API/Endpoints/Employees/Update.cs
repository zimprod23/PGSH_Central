using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Employees.Update;

namespace PGSH.API.Endpoints.Employees;

public sealed class UpdateEmployee : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("employees/{id:guid}", async (
            Guid id, UpdateEmployeeRequest request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateEmployeeCommand(
                id,
                request.Email,
                request.FirstName,
                request.LastName,
                request.CIN,
                request.PPR,
                request.Label,
                request.Grade,
                request.Position,
                request.WorkPlace,
                request.Gender,
                request.DateOfBirth,
                request.PlaceOfBirth,
                request.FullAddress,
                request.PvSignatureDate);

            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Employees)
        .RequireAuthorization();
    }

    public sealed record UpdateEmployeeRequest(
        string Email,
        string FirstName,
        string LastName,
        string? CIN,
        string? PPR,
        string? Label,
        Domain.Employees.Grade Grade,
        Domain.Employees.Position? Position,
        Domain.Employees.WorkPlace? WorkPlace,
        Domain.Users.Gender Gender,
        DateOnly? DateOfBirth,
        string? PlaceOfBirth,
        string? FullAddress,
        DateOnly? PvSignatureDate);
}
