using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed record ServicePeriodCompletedDomainEvent(
    Guid AssignmentId,
    Guid PeriodId) : IDomainEvent;
