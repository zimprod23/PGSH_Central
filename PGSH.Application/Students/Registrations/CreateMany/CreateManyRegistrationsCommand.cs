using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.CreateMany;

public sealed record CreateManyRegistrationsCommand(
    List<Guid> StudentIds,
    int AcademicYearId,
    int LevelId,
    string Status = "Pending") : ICommand<BulkResponse<Guid, Guid>>;