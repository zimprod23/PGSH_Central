using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Students.Registrations.Create;

public sealed record CreateRegistrationCommand(
    Guid StudentId,
    DateOnly AcademicYear,
    int LevelId,
    string Status = "Pending") :ICommand<Guid>;

