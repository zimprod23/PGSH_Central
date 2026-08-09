using PGSH.Domain.Registrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
using PGSH.Domain.Common.Utils;

namespace PGSH.Infrastructure.Registrations;

internal sealed class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public void Configure(EntityTypeBuilder<Registration> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
               .HasConversion<string>()
               .IsRequired();

        // Nullable: a year still running has no verdict, and neither has any of the six years the
        // legacy import carried — the column has to be able to say "nobody has pronounced yet".
        builder.Property(r => r.OutcomeSource)
               .HasConversion<string>();
        // Enum mapping
        //builder.Property(r => r.Level)
        //       .HasConversion<string>()
        //       .IsRequired();
        //builder.OwnsOne(s => s.Level, lvl =>
        //{
        //    lvl.Property(l => l.Label).HasMaxLength(100);
        //    lvl.Property(l => l.Year);
        //    lvl.Property(l => l.AcademicProgram);
        //});
        builder.HasOne(r => r.Level)
            .WithMany()
            .HasForeignKey(r => r.LevelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(e => e.failureReasons, fr =>
        {
            fr.Property(f => f.Description)
                .HasMaxLength(500);

            fr.Property(f => f.Notes)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null))
                .HasColumnType("jsonb");

            fr.Property(f => f.Cheat);
        });

        builder.HasOne(x => x.AcademicYear)
           .WithMany() 
           .HasForeignKey(x => x.AcademicYearId)
           .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AcademicGroup)
           .WithMany(x => x.Registrations)
           .HasForeignKey(x => x.AcademicGroupId)
           .OnDelete(DeleteBehavior.Restrict);

        // Relationship with Student
        builder.HasOne(r => r.Student)
               .WithMany(s => s.registrations)
               .HasForeignKey(r => r.StudentId)
               .OnDelete(DeleteBehavior.Cascade);

        // A student may hold at most one registration per academic year. The command handlers
        // already guard this (DuplicateRegistration); the unique index is the DB-level safety net.
        builder.HasIndex(r => new { r.StudentId, r.AcademicYearId })
               .HasDatabaseName("IX_Registration_Student_Year")
               .IsUnique();

        // One promotion = one (year, level), which is how the déliberation canvas, the réinscription
        // and every auto-arrange query reach registrations. LevelId had no index at all (Phase 13).
        builder.HasIndex(r => new { r.AcademicYearId, r.LevelId })
               .HasDatabaseName("IX_Registration_Year_Level");
    }
}

internal sealed class LevelConfiguration : IEntityTypeConfiguration<Level>
{
    public void Configure(EntityTypeBuilder<Level> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Label)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(l => l.Year)
               .IsRequired();

        builder.Property(l => l.AcademicProgram)
               .HasConversion<string>()
               .IsRequired();

        builder.HasIndex(l => new { l.Year, l.AcademicProgram })
               .IsUnique()
               .HasDatabaseName("IX_Level_Year_Program");
    }
}

internal sealed class AcademicYearConfiguration: IEntityTypeConfiguration<AcademicYear>
{
    public void Configure(EntityTypeBuilder<AcademicYear> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Label).IsRequired().HasMaxLength(20);
        builder.HasIndex(x => x.Label).IsUnique(); // Prevent duplicate years

        builder.HasMany(x => x.Groups)
               .WithOne(x => x.AcademicYear)
               .HasForeignKey(x => x.AcademicYearId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AcademicGroupConfiguration : IEntityTypeConfiguration<AcademicGroup>
{
    public void Configure(EntityTypeBuilder<AcademicGroup> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Label).IsRequired().HasMaxLength(100);

        builder.HasIndex(x => new { x.AcademicYearId, x.GroupNumber })
            .IsUnique()
            .HasDatabaseName("IX_AcademicGroup_Year_Number");

        builder.HasIndex(x => new { x.AcademicYearId, x.Label })
            .IsUnique()
            .HasDatabaseName("IX_AcademicGroup_Year_Label");

        builder.HasOne(x => x.Level)
            .WithMany()
            .HasForeignKey(x => x.LevelId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Registrations)
            .WithOne(x => x.AcademicGroup)
            .HasForeignKey(x => x.AcademicGroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}