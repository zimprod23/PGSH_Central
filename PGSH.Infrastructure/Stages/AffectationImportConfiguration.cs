using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Stages;

namespace PGSH.Infrastructure.Stages;

internal sealed class AffectationImportConfiguration : IEntityTypeConfiguration<AffectationImport>
{
    public void Configure(EntityTypeBuilder<AffectationImport> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Status).HasConversion<string>().IsRequired();
        builder.Property(i => i.FileName).HasMaxLength(260);
        builder.Property(i => i.AppliedAtUtc).IsRequired();

        // Restrict, like every other year-constituted pointer: an import belongs to the year it was
        // applied to, and deleting that year out from under it would leave a record of an act on a
        // promotion that no longer exists.
        builder.HasOne(i => i.AcademicYear)
               .WithMany()
               .HasForeignKey(i => i.AcademicYearId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Entries)
               .WithOne(e => e.AffectationImport)
               .HasForeignKey(e => e.AffectationImportId)
               .OnDelete(DeleteBehavior.Cascade);

        // « Quels imports ont touché cette promotion, le dernier d'abord » is the only way this table
        // is ever read.
        builder.HasIndex(i => new { i.AcademicYearId, i.LevelId, i.AppliedAtUtc })
               .HasDatabaseName("IX_AffectationImport_Year_Level_Applied");
    }
}

internal sealed class AffectationImportEntryConfiguration
    : IEntityTypeConfiguration<AffectationImportEntry>
{
    public void Configure(EntityTypeBuilder<AffectationImportEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Outcome).HasConversion<string>().IsRequired();

        // ⚠ InternshipAssignmentId is deliberately NOT a foreign key — see the entity. Undoing a
        // Created entry deletes that affectation, and a FK would either take this record with it
        // (losing the trace) or refuse the delete. It is indexed, because the reversal looks entries
        // up by it.
        builder.HasIndex(e => e.InternshipAssignmentId)
               .HasDatabaseName("IX_AffectationImportEntry_Assignment");

        builder.HasMany(e => e.ReplacedPeriods)
               .WithOne(p => p.AffectationImportEntry)
               .HasForeignKey(p => p.AffectationImportEntryId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReplacedPeriodConfiguration : IEntityTypeConfiguration<ReplacedPeriod>
{
    public void Configure(EntityTypeBuilder<ReplacedPeriod> builder)
    {
        // Named explicitly: the type has no DbSet of its own — it is reached through its entry — and
        // EF would otherwise call the table by the singular class name, alone among every table here.
        builder.ToTable("ReplacedPeriods");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.StartDate).IsRequired();
        builder.Property(p => p.EndDate).IsRequired();
        builder.Property(p => p.DelocalizationReason).HasMaxLength(500);

        // ⚠ No foreign key on ServiceId or CohortSlotAssignmentId, and that is the point of the table.
        // This is a *photograph* of a période that no longer exists, kept so it can be written back.
        // A FK would make the photograph refuse to be taken — or be deleted — whenever the service or
        // the grid cell moved on, which is precisely when the undo matters most. The reversal resolves
        // both by id and reports what it can no longer place.
    }
}
