using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Calendar;

namespace PGSH.Infrastructure.Calendar;

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Name).IsRequired().HasMaxLength(150);
        builder.Property(h => h.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.StartDate).IsRequired();
        builder.Property(h => h.EndDate).IsRequired();

        // Two holidays legitimately fall on one date — 21 août is both Fête de la Jeunesse and, some
        // years, inside Aïd — so the date alone cannot be the key. The name pins it, which is also what
        // makes re-seeding a year idempotent.
        builder.HasIndex(h => new { h.StartDate, h.Name }).IsUnique();

        // Every question asked of this table is "what falls in this window", so the range scan is the
        // access path worth indexing.
        builder.HasIndex(h => h.EndDate);

        builder.Ignore(h => h.DayCount);
    }
}
