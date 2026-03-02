using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Centers.GetMany;


public record GetCentersQuery(
int PageNumber = 1,
int PageSize = 10,
string? SearchTerm = null) : IQuery<PaginatedResponse<CenterSummaryResponse>>;
