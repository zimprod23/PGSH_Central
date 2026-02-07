using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Stages;

namespace PGSH.Infrastructure.Stages;

internal sealed class StageConfiguration : IEntityTypeConfiguration<Stage>
{
    public void Configure(EntityTypeBuilder<Stage> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Coefficient).IsRequired();
        builder.Property(s => s.DurationInDays).IsRequired();

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

internal sealed class CohortConfiguration : IEntityTypeConfiguration<Cohort>
{
    public void Configure(EntityTypeBuilder<Cohort> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Label).HasMaxLength(100);

        builder.HasOne(c => c.Stage)
               .WithMany()
               .HasForeignKey(c => c.StageId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CohortRotationTemplateConfiguration : IEntityTypeConfiguration<CohortRotationTemplate>
{
    public void Configure(EntityTypeBuilder<CohortRotationTemplate> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasOne(t => t.Cohort)
               .WithMany(c => c.RotationTemplates)
               .HasForeignKey(t => t.CohortId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Service)
               .WithMany()
               .HasForeignKey(t => t.ServiceId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InternshipAssignmentConfiguration
    : IEntityTypeConfiguration<InternshipAssignment>
{
    public void Configure(EntityTypeBuilder<InternshipAssignment> builder)
    {
        builder.HasKey(a => a.Id);

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

        builder.HasOne(a => a.Cohort)
                .WithMany()
                .HasForeignKey(a => a.CurrentCohortId)
                .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.MembershipHistory)
               .WithOne() // We don't necessarily need a navigation property back in Membership
               .HasForeignKey(m => m.InternshipAssignmentId)
               .OnDelete(DeleteBehavior.Cascade);

        // 2. Explicitly map the Service Periods
        builder.HasMany(a => a.ServicePeriods)
               .WithOne(p => p.InternshipAssignment) // Navigation back exists here
               .HasForeignKey(p => p.InternshipAssignmentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CohortMembershipConfiguration : IEntityTypeConfiguration<CohortMembership>
{
    public void Configure(EntityTypeBuilder<CohortMembership> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TransferReason).HasMaxLength(500);

        builder.HasOne(m => m.Cohort)
                .WithMany()
                .HasForeignKey(m => m.CohortId)
                .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InternshipAssignment>()
                .WithMany(a => a.MembershipHistory)
                .HasForeignKey(m => m.InternshipAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServicePeriodConfiguration : IEntityTypeConfiguration<ServicePeriod>
{
    public void Configure(EntityTypeBuilder<ServicePeriod> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasOne(p => p.InternshipAssignment)
                .WithMany(a => a.ServicePeriods)
                .HasForeignKey(p => p.InternshipAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Service)
                .WithMany()
                .HasForeignKey(p => p.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);

        // One-to-One relationship for Evaluation
        builder.HasOne(p => p.Evaluation)
                .WithOne(e => e.ServicePeriod)
                .HasForeignKey<ServiceEvaluation>(e => e.ServicePeriodId)
                .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServiceEvaluationConfiguration : IEntityTypeConfiguration<ServiceEvaluation>
{
    public void Configure(EntityTypeBuilder<ServiceEvaluation> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TotalScore).HasPrecision(5, 2);

        builder.HasMany(e => e.ObjectiveScores)
                .WithOne(o => o.ServiceEvaluation)
                .HasForeignKey(o => o.ServiceEvaluationId)
                .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ObjectiveScoreConfiguration : IEntityTypeConfiguration<ObjectiveScore>
{
    public void Configure(EntityTypeBuilder<ObjectiveScore> builder)
    {
        builder.HasKey(o => o.Id);

        builder.HasOne(o => o.StageObjective)
                .WithMany()
                .HasForeignKey(o => o.StageObjectiveId)
                .OnDelete(DeleteBehavior.Restrict);
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

        builder.HasOne(a => a.ServicePeriod)
                .WithMany(p => p.Attendance)
                .HasForeignKey(a => a.ServicePeriodId)
                .OnDelete(DeleteBehavior.Cascade);
        }
}

internal sealed class StageObjectiveConfiguration : IEntityTypeConfiguration<StageObjective>
{
    public void Configure(EntityTypeBuilder<StageObjective> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Label).IsRequired().HasMaxLength(200);

        builder.HasOne(o => o.Stage)
               .WithMany(s => s.Objectives)
               .HasForeignKey(o => o.StageId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

