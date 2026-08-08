using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Stages;

namespace PGSH.Infrastructure.Stages;

internal sealed class CurriculumConfiguration : IEntityTypeConfiguration<Curriculum>
{
    public void Configure(EntityTypeBuilder<Curriculum> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Reference).HasMaxLength(200);

        builder.HasOne(c => c.Level)
               .WithMany()
               .HasForeignKey(c => c.LevelId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.CnpnVersion)
               .WithMany(v => v.Curricula)
               .HasForeignKey(c => c.CnpnVersionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Stages)
               .WithOne(s => s.Curriculum)
               .HasForeignKey(s => s.CurriculumId)
               .OnDelete(DeleteBehavior.Cascade);

        // One requirement set per level per text — the aggregate's identity. Was (level, year) until
        // arrêté 1650.25 put two texts in the same year; see Curriculum's remarks.
        builder.HasIndex(c => new { c.CnpnVersionId, c.LevelId })
               .IsUnique()
               .HasDatabaseName("IX_Curriculum_Version_Level");
    }
}

internal sealed class CnpnVersionConfiguration : IEntityTypeConfiguration<CnpnVersion>
{
    public void Configure(EntityTypeBuilder<CnpnVersion> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Code).HasMaxLength(50).IsRequired();
        builder.Property(v => v.Label).HasMaxLength(200).IsRequired();
        builder.Property(v => v.Reference).HasMaxLength(300);
        builder.Property(v => v.AcademicProgram).HasConversion<string>().IsRequired();

        // Restrict: an academic year that anchors a CNPN's scope cannot be deleted out from under it.
        builder.HasOne(v => v.AppliesToEntrantsFromAcademicYear)
               .WithMany()
               .HasForeignKey(v => v.AppliesToEntrantsFromAcademicYearId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => new { v.AcademicProgram, v.Code })
               .IsUnique()
               .HasDatabaseName("IX_CnpnVersion_Program_Code");
    }
}

internal sealed class CurriculumStageConfiguration : IEntityTypeConfiguration<CurriculumStage>
{
    public void Configure(EntityTypeBuilder<CurriculumStage> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Coefficient).IsRequired();
        builder.Property(s => s.DurationInDays).IsRequired();

        // Restrict, not Cascade: a stage that any CNPN ever required cannot be deleted out from under
        // the historical record it belongs to.
        builder.HasOne(s => s.Stage)
               .WithMany()
               .HasForeignKey(s => s.StageId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.CurriculumId, s.StageId })
               .IsUnique()
               .HasDatabaseName("IX_CurriculumStage_Curriculum_Stage");
    }
}
