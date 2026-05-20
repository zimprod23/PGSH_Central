using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;

namespace PGSH.Application.Students.Registrations.Update;

public sealed record UpdateRegistrationCommand(
    Guid RegistrationId,
    Guid StudentId,
    RegistrationStatus Status,
    int AcademicYearId,
    int LevelId,
    string? FailureDescription = null,
    List<string>? FailureNotes = null,
    bool? Cheat = null) : ICommand;
