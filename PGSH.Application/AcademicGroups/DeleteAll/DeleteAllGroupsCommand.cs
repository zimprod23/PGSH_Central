using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.DeleteAll;

public sealed record DeleteAllGroupsCommand(int AcademicYearId) : ICommand<int>;
