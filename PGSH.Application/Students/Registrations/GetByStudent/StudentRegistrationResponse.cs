namespace PGSH.Application.Students.Registrations.GetByStudent;

public sealed record StudentRegistrationResponse(
    Guid Id,
    int AcademicYearId,
    string AcademicYear,
    int LevelId,
    string? LevelLabel,
    string Status,
    bool HasFailures,
    string? FailureDescription);