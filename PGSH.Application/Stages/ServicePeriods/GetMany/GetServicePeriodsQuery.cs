using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.ServicePeriods.GetMany;

public sealed record GetServicePeriodsQuery(
    Guid? AssignmentId = null,
    int? ServiceId = null,
    int? CohortId = null,
    bool? IsComplete = null,
    int PageNumber = 1,
    int PageSize = 100) : IQuery<PaginatedResponse<ServicePeriodResponse>>;
