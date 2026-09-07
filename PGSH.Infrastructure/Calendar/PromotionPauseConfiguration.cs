using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Calendar;

namespace PGSH.Infrastructure.Calendar;

internal sealed class PromotionPauseConfiguration : IEntityTypeConfiguration<PromotionPause>
{
    public void Configure(EntityTypeBuilder<PromotionPause> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Reason).IsRequired().HasMaxLength(300);
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.StartDate).IsRequired();
        builder.Property(p => p.EndDate).IsRequired();
        builder.Property(p => p.RecordedOn).IsRequired();

        // ⚠ Restrict on both sides. A window is scoped to (année, niveau) and means nothing without
        // either, so cascading it away with the year would silently take the calendar the year's
        // créneaux were laid against — and deleting a level out from under a declared exam session
        // should refuse, not succeed quietly.
        builder.HasOne(p => p.AcademicYear)
            .WithMany()
            .HasForeignKey(p => p.AcademicYearId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Level)
            .WithMany()
            .HasForeignKey(p => p.LevelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Every question asked of this table is "what has this promotion declared", and the answer is
        // then scanned by date. Non-unique on purpose: a promotion legitimately declares several windows
        // in a year, and the rule that they may not *overlap* is not something an index can state.
        builder.HasIndex(p => new { p.AcademicYearId, p.LevelId, p.StartDate });

        builder.Ignore(p => p.DayCount);
        builder.Ignore(p => p.Scope);
    }
}
