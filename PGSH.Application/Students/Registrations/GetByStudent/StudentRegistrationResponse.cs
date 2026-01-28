namespace PGSH.Application.Students.Registrations.GetByStudent;

public sealed record StudentRegistrationResponse(
    Guid Id,
    DateOnly AcademicYear,
    int LevelId,
    string Status,
    bool HasFailures, // Useful for showing a "Warning" icon in the UI
    string? FailureDescription);