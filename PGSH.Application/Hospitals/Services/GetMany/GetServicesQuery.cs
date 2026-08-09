using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.GetMany;

/// <summary>
/// <paramref name="AdmitsLevelId"/> narrows to the services that would actually take one promotion —
/// those holding a quota for it, plus every unrestricted service, since those take all comers. It is
/// what the planning screens need to ask: "where can this level go?"
/// </summary>
public record GetServicesQuery(
    int? HospitalId = null,
    ServiceType? ServiceType = null,
    Guid? ServiceChefId = null,
    int? AdmitsLevelId = null,
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null) : IQuery<PaginatedResponse<ServiceSummaryResponse>>;