using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed record StudentDelocalizedDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    int  StageId,
    int  ServiceId,
    string Reason) : IDomainEvent;
