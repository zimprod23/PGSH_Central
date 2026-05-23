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

        builder.HasMany(s => s.Objectives)
               .WithOne(o => o.Stage)
               .HasForeignKey(o => o.StageId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Slots)
               .WithOne(sl => sl.Stage)
               .HasForeignKey(sl => sl.StageId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CohortConfiguration : IEntityTypeConfiguration<Cohort>
{
    public void Configure(EntityTypeBuilder<Cohort> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Label).IsRequired().HasMaxLength(100);

        builder.HasOne(x => x.AcademicGroup)
           .WithMany(x => x.Cohorts)
           .HasForeignKey(x => x.AcademicGroupId)
           .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Stage)
               .WithMany()
               .HasForeignKey(c => c.StageId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StageSlotConfiguration : IEntityTypeConfiguration<StageSlot>
{
    public void Configure(EntityTypeBuilder<StageSlot> builder)
    {
        builder.HasKey(s => s.Id);

        builder.HasOne(s => s.Stage)
               .WithMany(st => st.Slots)
               .HasForeignKey(s => s.StageId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.StageId, s.PeriodNumber })
               .IsUnique()
               .HasDatabaseName("IX_StageSlot_Stage_Period");
    }
}

internal sealed class CohortSlotAssignmentConfiguration : IEntityTypeConfiguration<CohortSlotAssignment>
{
    public void Configure(EntityTypeBuilder<CohortSlotAssignment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasOne(a => a.Cohort)
               .WithMany(c => c.SlotAssignments)
               .HasForeignKey(a => a.CohortId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.StageSlot)
               .WithMany(s => s.Assignments)
               .HasForeignKey(a => a.StageSlotId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Service)
               .WithMany()
               .HasForeignKey(a => a.ServiceId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.CohortId, a.StageSlotId })
               .IsUnique()
               .HasDatabaseName("IX_CohortSlotAssignment_Cohort_Slot");
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

        //builder.HasOne(a => a.Cohort)
        //        .WithMany()
        //        .HasForeignKey(a => a.CurrentCohortId)
        //        .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Cohort)
               .WithMany(c => c.Assignments) // lie explicitement la navigation vers Cohort.Assignments
               .HasForeignKey(a => a.CurrentCohortId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.MembershipHistory)
               .WithOne()
               .HasForeignKey(m => m.InternshipAssignmentId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.ServicePeriods)
               .WithOne(p => p.InternshipAssignment)
               .HasForeignKey(p => p.InternshipAssignmentId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.RegistrationId)
               .HasDatabaseName("IX_InternshipAssignment_RegistrationId");

        builder.HasIndex(a => a.CurrentCohortId)
               .HasDatabaseName("IX_InternshipAssignment_CohortId");
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

        builder.HasIndex(m => m.InternshipAssignmentId)
               .HasDatabaseName("IX_CohortMembership_AssignmentId");
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

        builder.HasOne(p => p.CohortSlotAssignment)
                .WithMany()
                .HasForeignKey(p => p.CohortSlotAssignmentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.ServiceId)
               .HasDatabaseName("IX_ServicePeriod_ServiceId");

        builder.HasIndex(p => p.InternshipAssignmentId)
               .HasDatabaseName("IX_ServicePeriod_AssignmentId");

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

        builder.HasIndex(a => new { a.ServicePeriodId, a.Date })
               .IsUnique()
               .HasDatabaseName("IX_AttendanceRecord_Period_Date");
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

