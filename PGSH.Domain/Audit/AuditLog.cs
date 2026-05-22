namespace PGSH.Domain.Audit;

public sealed class AuditLog
{
    public Guid     Id                { get; set; } = Guid.NewGuid();
    public Guid?    PerformedByUserId { get; set; }
    public string   Action            { get; set; } = string.Empty; // e.g. "STUDENT_TRANSFERRED"
    public string   EntityType        { get; set; } = string.Empty; // e.g. "Registration"
    public string?  EntityId          { get; set; }                 // PK of the affected entity
    public string?  Metadata          { get; set; }                 // JSON: before/after/context
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
}
