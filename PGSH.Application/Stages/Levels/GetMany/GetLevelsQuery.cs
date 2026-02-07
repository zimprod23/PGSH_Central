using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Students.GetById;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Levels.GetMany;

public sealed record GetLevelsQuery(
    string? SearchTerm,
    int? AcademicProgram,
    int PageNumber = 1,
    int PageSize = 20): IQuery<PaginatedResponse<LevelResponse>>;
