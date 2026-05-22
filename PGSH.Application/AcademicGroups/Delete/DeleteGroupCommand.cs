using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Delete;

public sealed record DeleteGroupCommand(int Id)
    : ICommand, IAuditableCommand
{
    public string  AuditAction     => "GROUP_DELETED";
    public string  AuditEntityType => "AcademicGroup";
    public string? AuditEntityId   => Id.ToString();
    public string? AuditMetadata   => null;
}
