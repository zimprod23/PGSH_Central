using MediatR;
using PGSH.Application.Students.Create;
using PGSH.Domain.Users;
using PGSH.Domain.Students;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;

namespace PGSH.API.Endpoints.Students;

public sealed class Create : IEndpoint
{
    // On définit le Request avec Academy et Province (nullable ou non selon votre métier)
    public sealed record Request(
        string Email,
        string FirstName,
        string LastName,
        string? CIN,
        int Gender,
        int CivilStatus,
        int NationalityStatus,
        DateOnly? DateOfBirth,
        string? PlaceOfBirth,
        string? FullAddress,
        string CNE,
        string Appogee,
        decimal AccessGrade,
        int AcademicProgram,
        int BacSeries,
        string BacYear,
        int? Academy,   // Ajouté
        int? Province,  // Ajouté
        int? Ranking);  // Ajouté

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("students", async (Request request, ISender sender, CancellationToken ct) =>
        {
            // On mappe chaque champ vers le Command
            var command = new CreateStudentCommand(
                // The order must match your record definition exactly:
                Email: request.Email,
                FirstName: request.FirstName,
                LastName: request.LastName,
                CIN: request.CIN,
                CNE: request.CNE, // Argument 5 was failing here
                Appogee: request.Appogee,
                AccessGrade: request.AccessGrade,
                AcademicProgram: (AcademicProgram)request.AcademicProgram,
                BacSeries: (BacSeries)request.BacSeries,
                BacYear: request.BacYear,
                Gender: (Gender)request.Gender,
                CivilStatus: (CivilStatus)request.CivilStatus,
                NationalityStatus: (NationalityStatus)request.NationalityStatus,
                FullAddress: request.FullAddress,
                PlaceOfBirth: request.PlaceOfBirth,
                DateOfBirth: request.DateOfBirth,
                Academy: request.Academy.HasValue ? (Academy)request.Academy.Value : null,
                Province: request.Province.HasValue ? (Province)request.Province.Value : null,
                Ranking: request.Ranking
            );

            Result<Guid> result = await sender.Send(command, ct);

            return result.Match(id => Results.Created($"/students/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Students);
    }
}