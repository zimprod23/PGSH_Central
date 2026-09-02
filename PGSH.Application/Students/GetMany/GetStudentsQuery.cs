using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
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
    // The verdict recorded on the year's registration. ⚠ Read on the *same* registration as the
    // level and the year, never as a second independent condition — see the handler. It is what
    // makes the 1 217 diplômés of a promotion findable from the roll instead of only from a file.
    RegistrationStatus? Status = null,
    int PageNumber = 1,
    int PageSize = 10): IQuery<PaginatedResponse<StudentSummaryResponse>>;
