namespace PGSH.Application.Students.GetMany;

public record StudentSummaryResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? CNE,
    string Appogee,
    string AcademicProgram,
    string? CIN,
    string? CurrentLevelLabel,
    string? CurrentGroupLabel,
    string? CurrentStatus);
