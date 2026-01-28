using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.Create;

public record CreateStudentCommand(
    string Email,
    string FirstName,
    string LastName,
    string CIN,
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
    int? Ranking) : ICommand<Guid>;