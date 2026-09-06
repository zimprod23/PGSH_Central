using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed record StudentDelocalizedDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    int  StageId,
    int  ServiceId,
    string Reason) : IDomainEvent;

/// <summary>
/// A délocalisation was walked back and the student returned to the répartition. Raised so the
/// dossier says so: a délocalisation entered by mistake and silently removed would otherwise leave
/// the student's timeline claiming he served the stage abroad, with nothing after it.
/// </summary>
public sealed record DelocalizationCancelledDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    int  StageId,
    int  ServiceId) : IDomainEvent;
