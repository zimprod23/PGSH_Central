using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.GetMany;

public record GetStudentsQuery(
    string? SearchTerm,
    string? CNE,
    string? Appogee,
    string? CIN,
    AcademicProgram? Program = null,
    // One promotion. ⚠ Read together with AcademicYearId on the *same* registration, never as a
    // second independent condition — see the handler.
    int? LevelId = null,
    // When set, the level/group/status columns reflect this academic year's registration
    // (blank when the student has none that year). Null keeps the most-recent registration.
    int? AcademicYearId = null,
    int PageNumber = 1,
    int PageSize = 10): IQuery<PaginatedResponse<StudentSummaryResponse>>;
