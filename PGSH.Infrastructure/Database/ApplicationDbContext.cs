using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Audit;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using PGSH.SharedKernel;

namespace PGSH.Infrastructure.Database;

public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    // The context is pooled (Aspire AddNpgsqlDbContext), so pooled instances are constructed from
    // the root provider — a captured IPublisher would resolve notification handlers from the root
    // and fail for handlers needing scoped services (e.g. IApplicationDbContext). IServiceScopeFactory
    // is a pool-safe singleton; we open a fresh scope per publish to resolve the scoped mediator.
    private readonly IServiceScopeFactory? _scopeFactory;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IServiceScopeFactory? scopeFactory = null)
        : base(options) => _scopeFactory = scopeFactory;

    // ===== Identity / Core =====
    public DbSet<User> Users { get; set; }

    // ===== Academic / People =====
    public DbSet<Student> Students { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<Registration> Registrations { get; set; }
    public DbSet<AcademicYear> AcademicYears { get; set; }
    public DbSet<AcademicGroup> AcademicGroups { get; set; }

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
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<ObjectiveScore> ObjectiveScores { get; set; }
    public DbSet<Cohort> Cohorts { get; set; }
    public DbSet<StageSlot> StageSlots { get; set; }
    public DbSet<CohortSlotAssignment> CohortSlotAssignments { get; set; }
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
        int result = await base.SaveChangesAsync(cancellationToken);
        await PublishDomainEventsAsync();
        return result;
    }

    private async Task PublishDomainEventsAsync()
    {
        if (_scopeFactory is null) return;

        var domainEvents = ChangeTracker
            .Entries<Entity>()
            .Select(entry => entry.Entity)
            .SelectMany(entity =>
            {
                var events = entity.DomainEvents;
                entity.ClearDomainEvents();
                return events;
            })
            .ToList();

        if (domainEvents.Count == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        foreach (IDomainEvent domainEvent in domainEvents)
        {
            await publisher.Publish(domainEvent);
        }
    }
}
