using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Students.GetById;

public record StudentResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? CIN,
    string Gender,
    string CivilStatus,
    string NationalityStatus,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string? FullAddress,
    string CNE,
    string Appogee,
    string AcademicProgram,
    string BacSeries,
    string BacYear,
    decimal AccessGrade,
    int? Ranking
    , StudentRegistrationSummary? CurrentRegistration
    );

public sealed record StudentRegistrationSummary(
    Guid Id,
    DateOnly AcademicYear,
    string Status,
    LevelResponse Level);

public sealed record LevelResponse(

    string? Label,
    int Year,
    string AcademicProgram
);
