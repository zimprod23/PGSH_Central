namespace PGSH.Application.Abstractions.Messaging;

public interface IAuditableCommand
{
    string  AuditAction     { get; }
    string  AuditEntityType { get; }
    string? AuditEntityId   { get; }
    string? AuditMetadata   { get; }
}
