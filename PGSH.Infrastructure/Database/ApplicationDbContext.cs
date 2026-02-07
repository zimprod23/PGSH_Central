using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Todos;
using PGSH.Domain.Users;
//using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.SharedKernel;
using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Builder;
using PGSH.Domain.Students;
using PGSH.Domain.Employees;
using PGSH.Domain.Stages;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using PGSH.Domain.Common.Utils;

namespace PGSH.Infrastructure.Database;

public sealed class ApplicationDbContext
    : DbContext, IApplicationDbContext
{
    private readonly IPublisher? _publisher;
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IPublisher? publisher = null) : base(options) => _publisher = publisher;
    // ===== Identity / Core =====
    public DbSet<User> Users { get; set; }
    public DbSet<TodoItem> TodoItems { get; set; }

    // ===== Academic / People =====
    public DbSet<Student> Students { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<Registration> Registrations { get; set; }

    // ===== Stages / Internships =====
    public DbSet<Stage> Stages { get; set; }
    public DbSet<InternshipAssignment> InternshipAssignments { get; set; }

    // ===== Attendance & Evaluation =====
    public DbSet<AttendanceRecord> AttendanceRecords { get; set; }
    public DbSet<StageObjective> StageObjectives { get; set; }

    // ===== Hospital =====
    public DbSet<Center> Centers { get; set; }
    public DbSet<Hospital> Hospitals { get; set; }
    public DbSet<Service> Services { get; set; }

    // ===== Academic Structure =====
    public DbSet<Level> Levels { get; set; }

    // ===== Audit / History =====
    public DbSet<History> Histories { get; set; }
    public DbSet<ObjectiveScore> ObjectiveScores { get; set; }
    public DbSet<Cohort> Cohorts { get; set; }
    public DbSet<CohortRotationTemplate> CohortRotationTemplates { get; set; }
    public DbSet<CohortMembership> CohortMembership { get; set; }
    public DbSet<ServiceEvaluation> ServiceEvaluation { get; set; }
    public DbSet<ServicePeriod> ServicePeriods { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        modelBuilder.HasDefaultSchema(Schemas.Default);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // When should you publish domain events?
        //
        // 1. BEFORE calling SaveChangesAsync
        //     - domain events are part of the same transaction
        //     - immediate consistency
        // 2. AFTER calling SaveChangesAsync
        //     - domain events are a separate transaction
        //     - eventual consistency
        //     - handlers can fail

        int result = await base.SaveChangesAsync(cancellationToken);

        await PublishDomainEventsAsync();

        return result;
    }

    private async Task PublishDomainEventsAsync()
    {
        if (_publisher == null) return;
        var domainEvents = ChangeTracker
            .Entries<Entity>()
            .Select(entry => entry.Entity)
            .SelectMany(entity =>
            {
                List<IDomainEvent> domainEvents = entity.DomainEvents;

                entity.ClearDomainEvents();

                return domainEvents;
            })
            .ToList();

        foreach (IDomainEvent domainEvent in domainEvents)
        {
            await _publisher.Publish(domainEvent);
        }
    }
}
