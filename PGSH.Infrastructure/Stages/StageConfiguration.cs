using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Stages;

namespace PGSH.Infrastructure.Stages;

internal sealed class StageConfiguration : IEntityTypeConfiguration<Stage>
{
    public void Configure(EntityTypeBuilder<Stage> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
               .IsRequired()
               .HasMaxLength(100);

        builder.Property(s => s.Description)
               .HasMaxLength(500);

        builder.Property(s => s.Coefficient)
               .IsRequired();

        builder.Property(s => s.DurationInDays)
               .IsRequired();

        //builder.OwnsOne(s => s.Level, lvl =>
        //{
        //    lvl.Property(l => l.Label).HasMaxLength(100);
        //    lvl.Property(l => l.Year);
        //    lvl.Property(l => l.AcademicProgram);
        //});
        builder.HasOne(r => r.Level)
            .WithMany()
            .HasForeignKey("LevelId") // shadow FK (pas dans la classe)
            .OnDelete(DeleteBehavior.Restrict);
        //builder.Property(s => s.Level)
        //       .HasConversion<string>()
        //       .IsRequired();
    }
}

internal sealed class StageGroupConfiguration : IEntityTypeConfiguration<StageGroup>
{
    public void Configure(EntityTypeBuilder<StageGroup> builder)
    {
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Label)
               .IsRequired()
               .HasMaxLength(100);

        builder.Property(g => g.Description)
               .HasMaxLength(500);

        // One Stage -> Many StageGroups
        builder.HasOne(g => g.Stage)
               .WithMany()
               .HasForeignKey(g => g.StageId)
               .OnDelete(DeleteBehavior.Cascade);

        // One StageGroup -> Many InternshipAssignments
        builder.HasMany(g => g.internshipAssignments)
               .WithOne(a => a.StageGroup)
               .HasForeignKey(a => a.StageGroupId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InternshipAssignmentConfiguration : IEntityTypeConfiguration<InternshipAssignment>
{
    public void Configure(EntityTypeBuilder<InternshipAssignment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.PlannedStart).IsRequired();
        builder.Property(a => a.PlannedEnd).IsRequired();

        builder.Property(a => a.Score)
               .HasPrecision(5, 2)
               .IsRequired();

        // Relationship to StageGroup
        builder.HasOne(a => a.StageGroup)
               .WithMany(g => g.internshipAssignments)
               .HasForeignKey(a => a.StageGroupId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Registration)
               .WithMany(r => r.InternshipAssignments) // you may need to add this collection in Registration
               .HasForeignKey(a => a.RegistrationId)
               .OnDelete(DeleteBehavior.Cascade);

        // Relationship to AssignmentPeriods
        builder.HasMany(a => a.Periods)
               .WithOne(p => p.InternshipAssignment)
               .HasForeignKey(p => p.AssignementId)
               .OnDelete(DeleteBehavior.Cascade);

        //// StageEvaluation is a complex type, use OwnsOne
        //builder.OwnsOne(a => a.evaluation, se =>
        //{
        //    se.Property(e => e.Score);
        //    se.Property(e => e.AssignmentResult)
        //      .HasConversion<string>()
        //      .IsRequired();
        //    se.Property(e => e.Notes)
        //      .HasConversion(
        //          v => string.Join(";", v ?? Array.Empty<string>()),
        //          v => v.Split(";", StringSplitOptions.RemoveEmptyEntries));
        //});
    }
}

internal sealed class AssignmentPeriodConfiguration : IEntityTypeConfiguration<AssignmentPeriod>
{
    public void Configure(EntityTypeBuilder<AssignmentPeriod> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Start).IsRequired();
        builder.Property(p => p.End).IsRequired();

        // Relationship to InternshipAssignment
        builder.HasOne(p => p.InternshipAssignment)
               .WithMany(a => a.Periods)
               .HasForeignKey(p => p.AssignementId)
               .OnDelete(DeleteBehavior.Cascade);

        // Relationship to Service
        builder.HasOne(p => p.Service)
               .WithMany(s => s.assignmentPeriods)
               .HasForeignKey(p => p.ServiceId)
               .OnDelete(DeleteBehavior.Restrict); // do not delete service if period deleted
    }
}