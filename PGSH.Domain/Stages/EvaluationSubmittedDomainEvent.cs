using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed record EvaluationSubmittedDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    Guid PeriodId,
    decimal TotalScore) : IDomainEvent;
