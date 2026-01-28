using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Domain.Users;

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
    int? Ranking);