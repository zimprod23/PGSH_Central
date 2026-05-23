using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.Update;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Students;
using PGSH.Domain.Users;

namespace PGSH.API.Endpoints.Students;

public sealed class UpdateStudent : IEndpoint
{
    public sealed record Request(
        string Email,
        string FirstName,
        string LastName,
        string? CIN,
        string CNE,
        string Appogee,
        decimal AccessGrade,
        AcademicProgram AcademicProgram,
        BacSeries BacSeries,
        string BacYear,
        Gender Gender,
        CivilStatus CivilStatus,
        NationalityStatus NationalityStatus,
        string? PlaceOfBirth,
        string? FullAddress,
        DateOnly? DateOfBirth,
        Academy? Academy,
        Province? Province,
        int? Ranking);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("students/{id:guid}", async (
            Guid id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateStudentCommand(
                id,
                request.Email,
                request.FirstName,
                request.LastName,
                request.CIN,
                request.CNE,
                request.Appogee,
                request.AccessGrade,
                request.AcademicProgram,
                request.BacSeries,
                request.BacYear,
                request.Gender,
                request.CivilStatus,
                request.NationalityStatus,
                request.PlaceOfBirth,
                request.FullAddress,
                request.DateOfBirth,
                request.Academy,
                request.Province,
                request.Ranking);

            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Students)
        .RequireAuthorization();
    }
}
