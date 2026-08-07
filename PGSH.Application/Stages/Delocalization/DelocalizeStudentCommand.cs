using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Delocalization;

public sealed record DelocalizeStudentCommand(
    Guid               RegistrationId,
    int                StageId,
    int                ServiceId,
    DateOnly           StartDate,
    DateOnly           EndDate,
    string             Reason,
    // Optional paper-validation verdict, recorded in the same step when the délocalisation is
    // logged after the student returns. Null = record the movement now, enter the verdict later.
    EvaluationOutcome? Outcome        = null,
    string?            FicheReference = null,
    Guid?              DemandeId      = null)
    : ICommand, IAuditableCommand
{
    public string  AuditAction     => "STUDENT_DELOCALIZED";
    public string  AuditEntityType => "Registration";
    public string? AuditEntityId   => RegistrationId.ToString();
    public string? AuditMetadata   =>
        $"{{\"stageId\":{StageId},\"serviceId\":{ServiceId},\"reason\":\"{Reason}\"}}";
}
