using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.GetMany;

public record GetServicesQuery(
    int? HospitalId = null,
    int? ServiceType = null,
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null) : IQuery<PaginatedResponse<ServiceSummaryResponse>>;