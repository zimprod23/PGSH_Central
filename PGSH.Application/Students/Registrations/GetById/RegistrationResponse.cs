using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.Registrations.GetById;

public sealed record RegistrationResponse(
    Guid Id,
    Guid StudentId,
    string StudentFullName,
    DateOnly AcademicYear,
    int LevelId,
    string Status,
    string? FailureDescription,
    List<string> FailureNotes,
    bool Cheat);
