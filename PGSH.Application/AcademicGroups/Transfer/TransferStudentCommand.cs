using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;

namespace PGSH.Application.AcademicGroups.Transfer;

public sealed record TransferStudentCommand(
    Guid         RegistrationId,
    int          TargetGroupId,
    string       Reason,
    TransferType Type   = TransferType.Temporary,
    int?         StageId = null)
    : ICommand, IAuditableCommand
{
    public string  AuditAction     => "STUDENT_TRANSFERRED";
    public string  AuditEntityType => "Registration";
    public string? AuditEntityId   => RegistrationId.ToString();
    public string? AuditMetadata   =>
        $"{{\"targetGroupId\":{TargetGroupId},\"type\":\"{Type}\",\"stageId\":{(StageId?.ToString() ?? "null")},\"reason\":\"{Reason}\"}}";
}
