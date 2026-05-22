using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Transfer;

public sealed record TransferStudentCommand(
    Guid   RegistrationId,
    int    TargetGroupId,
    string? Reason)
    : ICommand, IAuditableCommand
{
    public string  AuditAction     => "STUDENT_TRANSFERRED";
    public string  AuditEntityType => "Registration";
    public string? AuditEntityId   => RegistrationId.ToString();
    public string? AuditMetadata   => $"{{\"targetGroupId\":{TargetGroupId},\"reason\":\"{Reason}\"}}";
}
