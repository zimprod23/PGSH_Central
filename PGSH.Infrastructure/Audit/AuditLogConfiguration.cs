using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Audit;

namespace PGSH.Infrastructure.Audit;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.Metadata).HasColumnType("jsonb");

        builder.HasIndex(a => a.CreatedAt).HasDatabaseName("IX_AuditLog_CreatedAt");
        builder.HasIndex(a => a.PerformedByUserId).HasDatabaseName("IX_AuditLog_PerformedBy");
        builder.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("IX_AuditLog_Entity");
    }
}
