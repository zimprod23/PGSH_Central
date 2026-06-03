using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Empty;

public sealed record EmptyAllYearGroupsCommand(int AcademicYearId) : ICommand<int>;
