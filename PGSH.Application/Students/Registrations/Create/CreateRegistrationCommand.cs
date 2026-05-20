using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;

namespace PGSH.Application.Students.Registrations.Create;

public sealed record CreateRegistrationCommand(
    Guid StudentId,
    int AcademicYearId,
    int LevelId,
    RegistrationStatus Status = RegistrationStatus.Pending) : ICommand<Guid>;

