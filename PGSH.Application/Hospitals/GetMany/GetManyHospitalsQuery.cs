using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.GetMany;


public record GetHospitalsQuery(
    int? CenterId = null,
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null) : IQuery<PaginatedResponse<HospitalSummaryResponse>>;
