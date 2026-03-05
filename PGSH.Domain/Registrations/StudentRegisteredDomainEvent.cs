using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

public sealed record StudentRegisteredDomainEvent(
    Guid RegistrationId,
    Guid StudentId,
    int LevelId,
    int AcademicYear): IDomainEvent;
