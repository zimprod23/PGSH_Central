using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

public sealed record AssignRotationGroupsCommand(int AcademicYearId, int PartitionCount) : ICommand<int>;
