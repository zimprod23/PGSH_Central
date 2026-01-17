using PGSH.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PGSH.Domain.Employees;
using PGSH.Domain.Students;
using System.Text.Json;

namespace PGSH.Infrastructure.Users;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(u => u.UserName)
            .HasMaxLength(100);

        builder.Property(u => u.CIN)
            .HasMaxLength(20);

        builder.Property(u => u.FirstName)
            .HasMaxLength(100);

        builder.Property(u => u.LastName)
            .HasMaxLength(100);

        builder.OwnsOne(u => u.Address, a =>
        {
            a.Property(p => p.FullAddress).HasMaxLength(250);
            a.Property(p => p.City).HasMaxLength(100);
            a.Property(p => p.Street).HasMaxLength(100);
            a.Property(p => p.ZIP).HasMaxLength(20);
            a.Property(p => p.HouseNumber).HasMaxLength(20);
            a.Property(p => p.Country).HasMaxLength(100);

            // Optional: set column name prefix (otherwise EF will use Address_FullAddress, etc.)
            a.ToTable("Users"); // keep it in the same table, not a new one
        });
        builder.OwnsOne(u => u.Status, s =>
        {
            s.Property(p => p.CivilStatus)
                .HasConversion<string>() // store as text instead of int
                .HasMaxLength(50);

            s.Property(p => p.NationalityStatus)
                .HasConversion<string>()
                .HasMaxLength(50);
        });

        // Discriminator column
        builder.HasDiscriminator<string>("UserType")
         .HasValue<User>("User")
         .HasValue<Employee>("Employee")
         .HasValue<Student>("Student");


       
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.Property(e => e.PPR).HasMaxLength(50);
        builder.Property(e => e.Label).HasMaxLength(100);
        builder.Property(e => e.Grade).IsRequired();
        builder.Property(e => e.Position);
    }
}

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        // --- Student-specific properties ---
        builder.Property(s => s.CNE).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Appogee).HasMaxLength(50);
        builder.Property(s => s.BacYear).HasMaxLength(10);
        builder.Property(s => s.AccessGrade).HasPrecision(5, 2);
        builder.Property(s => s.AgreementType).HasDefaultValue(AgreementType.None);

        builder.Property(l => l.AcademicProgram)
              .HasConversion<string>()
              .IsRequired();

        // --- Collections ---
        builder.HasMany(s => s.registrations)
               .WithOne(r => r.Student)
               .HasForeignKey(r => r.StudentId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.history)
               .WithOne(h => h.Student)
               .HasForeignKey(h => h.StudentId)
               .OnDelete(DeleteBehavior.Cascade);

        // Optional: access mode for encapsulation
        builder.Navigation(s => s.registrations)
               .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(s => s.history)
               .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class HistoryConfiguration : IEntityTypeConfiguration<History>
{
    public void Configure(EntityTypeBuilder<History> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.CreatedAt).IsRequired();

        builder.Property(h => h.HistoryData)
               .HasConversion<string>()
               .IsRequired();

        builder.Property(h => h.Metadata)
             .HasConversion(
                 v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                 v => JsonSerializer.Deserialize<object>(v, (JsonSerializerOptions)null))
             .HasColumnType("jsonb"); // PostgreSQL native type

        builder.HasOne(h => h.Student)
               .WithMany(s => s.history)
               .HasForeignKey(h => h.StudentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}