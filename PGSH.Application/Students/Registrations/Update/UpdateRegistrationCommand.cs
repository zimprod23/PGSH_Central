using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Students.Registrations.Update;

public sealed record UpdateRegistrationCommand(
    Guid RegistrationId,
    Guid StudentId,
    string Status,
    DateOnly AcademicYear,
    int LevelId,
    string? FailureDescription = null,
    List<string>? FailureNotes = null,
    bool? Cheat = null): ICommand;
