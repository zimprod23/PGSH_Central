using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.GetMany;

public record StudentSummaryResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string CNE,
    string Appogee,
    string AcademicProgram,
    string? CIN);
