using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Registrations;

namespace PGSH.Infrastructure.Registrations;

internal sealed class RegistrationHoldConfiguration : IEntityTypeConfiguration<RegistrationHold>
{
    public void Configure(EntityTypeBuilder<RegistrationHold> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Reason)
               .HasConversion<string>()
               .IsRequired();

        builder.Property(h => h.Evidence)
               .HasMaxLength(1000)
               .IsRequired();

        builder.Property(h => h.ReleaseNote)
               .HasMaxLength(1000);

        // Cascade, and it is the one behaviour that is right here: a hold is a fact *about* a
        // registration and means nothing without it — the same bargain AcademicGroups strikes with
        // its year. Restrict would make deleting a mistyped registration a raw FK violation, i.e. a
        // 500 naming a table the user has never heard of.
        builder.HasOne(h => h.Registration)
               .WithMany(r => r.Holds)
               .HasForeignKey(h => h.RegistrationId)
               .OnDelete(DeleteBehavior.Cascade);

        // ⚠ Filtered on the *unreleased* holds only, and it is not merely an optimisation: every
        // planning read asks « does this registration carry an active hold? » (RegistrationHoldPolicy),
        // which is an EXISTS over exactly this set. Unfiltered, the index would be dominated by the
        // released rows, which are history and are never queried by the hot path.
        // ⚠ UNIQUE on (registration, reason) among the *unreleased* rows — one standing flag per
        // reason, any number of released ones behind it. It is the invariant `PlaceOnHold` states in
        // memory, and stating it here too is not redundancy: that check reads `Registration.Holds`,
        // and an un-Included collection is indistinguishable from an empty one, so a caller that
        // forgets the Include silently raises a duplicate. It did — the second réinscription upload
        // put a second absentee flag on all 1 267 of them, and the in-memory suite could not see it
        // (that provider fixes navigations up from the change tracker).
        //
        // Same bargain as IX_CnpnLevelEffectivity_Version_Level: a missed Include degrades to a
        // constraint violation instead of to silent duplication.
        builder.HasIndex(h => new { h.RegistrationId, h.Reason })
               .IsUnique()
               .HasFilter("\"ReleasedOn\" IS NULL")
               .HasDatabaseName("IX_RegistrationHold_Registration_Reason_Active");

        // The worklist reads « tous les signalements ouverts, motif par motif », so the reason leads.
        builder.HasIndex(h => new { h.Reason, h.RaisedOn })
               .HasDatabaseName("IX_RegistrationHold_Reason_RaisedOn");
    }
}
