using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Manage;

public sealed record AutoArrangeGroupsCommand(
    int LevelId,
    int AcademicYearId,
    int GroupSize) : ICommand<BulkResponse<Guid, int>>;