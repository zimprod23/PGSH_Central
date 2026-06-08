using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.ServicePeriods;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.MyServices;

/// <summary>
/// Service periods scoped to the services the current employee is chef of. The chef's
/// services are derived server-side from the identity — a <paramref name="ServiceId"/>
/// the caller does not lead is silently ignored, so a chef can never read another
/// chef's worklist.
/// </summary>
public sealed record GetMyServicePeriodsQuery(
    int? ServiceId = null,
    bool? IsComplete = null,
    int PageNumber = 1,
    int PageSize = 100) : IQuery<PaginatedResponse<ServicePeriodResponse>>;
