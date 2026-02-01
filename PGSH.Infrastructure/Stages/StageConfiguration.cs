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

        // Relationship with Level using explicit Key
        builder.HasOne(s => s.Level)
               .WithMany()
               .HasForeignKey(s => s.LevelId) // Now a real property
               .OnDelete(DeleteBehavior.Restrict);

        // Explicit relationship with Objectives
        builder.HasMany(s => s.Objectives)
               .WithOne(o => o.Stage)
               .HasForeignKey(o => o.StageId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StageGroupConfiguration
    : IEntityTypeConfiguration<StageGroup>
{
    public void Configure(EntityTypeBuilder<StageGroup> builder)
    {
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Label)
               .IsRequired()
               .HasMaxLength(100);

        builder.HasOne(g => g.Stage)
               .WithMany()
               .HasForeignKey(g => g.StageId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(g => g.Periods)
               .WithOne(p => p.StageGroup)
               .HasForeignKey(p => p.StageGroupId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(g => g.InternshipAssignments)
               .WithOne(a => a.StageGroup)
               .HasForeignKey(a => a.StageGroupId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}


internal sealed class StageGroupPeriodConfiguration
    : IEntityTypeConfiguration<StageGroupPeriod>
{
    public void Configure(EntityTypeBuilder<StageGroupPeriod> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Start).IsRequired();
        builder.Property(p => p.End).IsRequired();

        builder.HasOne(p => p.Service)
               .WithMany()
               .HasForeignKey(p => p.ServiceId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}



internal sealed class InternshipAssignmentConfiguration
    : IEntityTypeConfiguration<InternshipAssignment>
{
    public void Configure(EntityTypeBuilder<InternshipAssignment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.PlannedStart).IsRequired();
        builder.Property(a => a.PlannedEnd).IsRequired();

        builder.Property(a => a.Status)
               .HasConversion<string>()
               .IsRequired();

        builder.Property(a => a.FinalScore)
               .HasPrecision(5, 2);

        builder.Property(a => a.Result)
               .HasConversion<string>();

        builder.HasOne(a => a.Registration)
               .WithMany(r => r.InternshipAssignments)
               .HasForeignKey(a => a.RegistrationId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.AttendanceRecords)
               .WithOne(ar => ar.InternshipAssignment)
               .HasForeignKey(ar => ar.InternshipAssignmentId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.PeriodEvaluations)
               .WithOne(pe => pe.InternshipAssignment)
               .HasForeignKey(pe => pe.InternshipAssignmentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}




internal sealed class AttendanceConfiguration
    : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Date).IsRequired();

        builder.Property(a => a.Status)
               .HasConversion<string>()
               .IsRequired();

        builder.HasOne(a => a.StageGroupPeriod)
               .WithMany()
               .HasForeignKey(a => a.StageGroupPeriodId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StageObjectiveConfiguration
    : IEntityTypeConfiguration<StageObjective>
{
    public void Configure(EntityTypeBuilder<StageObjective> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Label)
               .IsRequired()
               .HasMaxLength(200);

        builder.HasOne(o => o.Stage)
               .WithMany(s => s.Objectives)
               .HasForeignKey(o => o.StageId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}


internal sealed class PeriodEvaluationConfiguration
    : IEntityTypeConfiguration<PeriodEvaluation>
{
    public void Configure(EntityTypeBuilder<PeriodEvaluation> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasOne(e => e.StageGroupPeriod)
               .WithMany()
               .HasForeignKey(e => e.StageGroupPeriodId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.ObjectiveEvaluations)
               .WithOne(oe => oe.PeriodEvaluation)
               .HasForeignKey(oe => oe.PeriodEvaluationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
