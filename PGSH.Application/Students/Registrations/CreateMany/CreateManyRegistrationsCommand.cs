using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.CreateMany;

public sealed record CreateManyRegistrationsCommand(
    List<Guid> StudentIds,
    int AcademicYearId,
    int LevelId,
    RegistrationStatus Status = RegistrationStatus.Pending) : ICommand<BulkResponse<Guid, Guid>>;