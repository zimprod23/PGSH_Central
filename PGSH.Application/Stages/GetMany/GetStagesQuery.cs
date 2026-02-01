using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.GetMany;

public record GetStagesQuery(
    string? SearchTerm,
    int? LevelId,
    int PageNumber = 1,
    int PageSize = 10) : IQuery<PaginatedResponse<StageSummaryResponse>>;