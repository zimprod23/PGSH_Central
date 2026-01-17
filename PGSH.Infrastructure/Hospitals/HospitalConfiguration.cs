using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using System.Reflection.Emit;

namespace PGSH.Infrastructure.Hospitals;

internal sealed class CenterConfiguration : IEntityTypeConfiguration<Center>
{
    public void Configure(EntityTypeBuilder<Center> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
               .IsRequired()
               .HasMaxLength(100);

        builder.Property(c => c.City)
               .HasMaxLength(50);

        builder.Property(c => c.CenterType)
               .HasConversion<string>()
               .IsRequired();

        // One Center -> Many Hospitals
        builder.HasMany(c => c.Hospitals)
               .WithOne(h => h.Center)
               .HasForeignKey(h => h.CenterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.OwnsOne(a => a.LocalisationMaps, loc =>
        {
            loc.Property(l => l.x)
                .HasColumnName("X")
                .HasMaxLength(50);

            loc.Property(l => l.y)
                .HasColumnName("Y")
                .HasMaxLength(50);

            loc.Property(l => l.z)
                .HasColumnName("Z")
                .HasMaxLength(50);
        });
    }
}

internal sealed class HospitalConfiguration : IEntityTypeConfiguration<Hospital>
{
    public void Configure(EntityTypeBuilder<Hospital> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Name)
               .IsRequired()
               .HasMaxLength(100);

        builder.Property(h => h.City)
               .IsRequired()
               .HasMaxLength(50);

        builder.Property(h => h.Description)
               .HasMaxLength(500);

        builder.Property(h => h.HospitalType)
               .HasConversion<string>()
               .IsRequired();

        builder.Property(h => h.Email)
               .HasMaxLength(100);

        // One Hospital -> Many Services
        builder.HasMany(h => h.services)
               .WithOne(s => s.Hospital)
               .HasForeignKey(s => s.HospitalId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.OwnsOne(a => a.LocalisationMaps, loc =>
        {
            loc.Property(l => l.x)
                .HasColumnName("X")
                .HasMaxLength(50);

            loc.Property(l => l.y)
                .HasColumnName("Y")
                .HasMaxLength(50);

            loc.Property(l => l.z)
                .HasColumnName("Z")
                .HasMaxLength(50);
        });
    }
}

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
               .IsRequired()
               .HasMaxLength(100);

        builder.Property(s => s.Description)
               .HasMaxLength(500);

        builder.Property(s => s.ServiceType)
               .HasConversion<string>()
               .IsRequired();

        builder.Property(s => s.Capacity)
               .IsRequired();

        // One Service -> Many Employees (Staff)
        builder.HasMany(s => s.Staff)
               .WithMany(); // optional: if Employee has no navigation to Service

        // One Service -> Many AssignmentPeriods
        builder.HasMany(s => s.assignmentPeriods)
               .WithOne()
               .HasForeignKey("ServiceId")
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.ServiceChef)
                   .WithMany()
                   // 2. Specify the public Foreign Key property
                   .HasForeignKey(s => s.ServiceChefId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.Restrict);
    }
}