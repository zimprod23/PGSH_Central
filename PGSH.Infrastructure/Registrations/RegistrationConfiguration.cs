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

        // Nullable for the same reason: the six imported years were backfilled from the student's
        // stamp where he had one, and ~2,200 enrolled students carry none at all. Restrict — a text
        // that governed a year cannot be deleted out from under the registrations it governed, which
        // is also what makes DeleteCnpnVersionCommand's gate a refusal rather than a 500.
        builder.Property(r => r.CnpnSource)
               .HasConversion<string>();

        builder.HasOne(r => r.CnpnVersion)
               .WithMany()
               .HasForeignKey(r => r.CnpnVersionId)
               .OnDelete(DeleteBehavior.Restrict);
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

        // ⚠ No index is declared here for AcademicGroupId or LevelId on purpose: EF Core creates one
        // per foreign key by convention (IX_Registrations_AcademicGroupId, IX_Registrations_LevelId),
        // so the two "missing FK index" items carried in Phase 13 were never real. Declaring them
        // renames the existing indexes and buys nothing.
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

        // ⚠ "The current year" is a singleton the whole application reads as one: AcademicYearResolver
        // takes the *first* row flagged current and every handler that omits a year gets it, so two
        // rows flagged at once means two different screens quietly disagree about which promotion they
        // are showing — with nothing to indicate it. CreateAcademicYear demotes the others, but that
        // is one write path guarding an invariant of the table; the partial index is the invariant.
        builder.HasIndex(x => x.IsCurrent)
               .IsUnique()
               .HasFilter("\"IsCurrent\"")
               .HasDatabaseName("IX_AcademicYear_IsCurrent");

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

        // ⚠ The level is part of the key, not an attribute of the row. The faculty numbers its groups
        // per promotion — the 3rd year runs 1-80, the 5th year 1-60, the 6th year 1-100, all at the
        // same time — so a number is only meaningful alongside the promotion it counts within. Keying
        // on (year, number) alone forced those three numberings into one, which is how the legacy
        // import came to fold the 3rd year's "Groupe 1" and the 5th year's into a single roster: one
        // row, five promotions, and a planning guard that then refused the 5th year because the 3rd
        // was already placed over those dates.
        //
        // Nulls are not distinct, so "no promotion yet" is itself a bucket and the year's single
        // « Non réparti » cannot be duplicated.
        builder.HasIndex(x => new { x.AcademicYearId, x.LevelId, x.GroupNumber })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("IX_AcademicGroup_Year_Level_Number");

        // The label is keyed the same way, and for the same reason: it is what an admin reads, so it
        // has to distinguish two rosters *of one promotion* — and only that. Held to (year, label),
        // the obvious name for the 4th year's first roster, « Groupe 1 », was already taken by the 3rd
        // year's, so the promotion the faculty numbers 1-60 could not be named the way it is printed.
        builder.HasIndex(x => new { x.AcademicYearId, x.LevelId, x.Label })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("IX_AcademicGroup_Year_Level_Label");

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
/// <summary>
/// The nominative exception to « la dernière année ne commence pas avant que tout soit validé ».
/// </summary>
internal sealed class FinalYearEntryWaiverConfiguration : IEntityTypeConfiguration<FinalYearEntryWaiver>
{
    public void Configure(EntityTypeBuilder<FinalYearEntryWaiver> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(w => w.OutstandingSummary).HasMaxLength(1000);

        // Cascade from the student: a waiver for somebody who no longer exists explains nothing.
        builder.HasOne(w => w.Student)
               .WithMany()
               .HasForeignKey(w => w.StudentId)
               .OnDelete(DeleteBehavior.Cascade);

        // Restrict on the year, like every other year-anchored row: an academic year cannot be
        // deleted out from under the exceptions granted for it.
        builder.HasOne(w => w.AcademicYear)
               .WithMany()
               .HasForeignKey(w => w.AcademicYearId)
               .OnDelete(DeleteBehavior.Restrict);

        // One waiver per student per year. A second would say the same thing twice, and the
        // réinscription only ever asks whether one exists.
        builder.HasIndex(w => new { w.StudentId, w.AcademicYearId })
               .IsUnique()
               .HasDatabaseName("IX_FinalYearEntryWaiver_Student_Year");
    }
}
