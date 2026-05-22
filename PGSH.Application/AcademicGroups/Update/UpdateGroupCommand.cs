using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Update;

public sealed record UpdateGroupCommand(
    int     Id,
    string  Label,
    string? GeographicZone)
    : ICommand, IAuditableCommand
{
    public string  AuditAction     => "GROUP_UPDATED";
    public string  AuditEntityType => "AcademicGroup";
    public string? AuditEntityId   => Id.ToString();
    public string? AuditMetadata   => null;
}
