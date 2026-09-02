using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Audit;
using PGSH.Domain.Calendar;
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
    public DbSet<ServiceLevelCapacity> ServiceLevelCapacities { get; set; }

    // ===== Academic Structure =====
    public DbSet<Level> Levels { get; set; }

    // ===== Audit / History =====
    public DbSet<History> Histories { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<ObjectiveScore> ObjectiveScores { get; set; }
    public DbSet<FinalYearEntryWaiver> FinalYearEntryWaivers { get; set; }
    public DbSet<RegistrationHold> RegistrationHolds { get; set; }
    public DbSet<PriorEnrolment> PriorEnrolments { get; set; }
    public DbSet<Cohort> Cohorts { get; set; }
    public DbSet<CnpnVersion> CnpnVersions { get; set; }
    public DbSet<CnpnLevelEffectivity> CnpnLevelEffectivities { get; set; }
    public DbSet<Curriculum> Curriculums { get; set; }
    public DbSet<CurriculumStage> CurriculumStages { get; set; }
    public DbSet<StageSlot> StageSlots { get; set; }
    public DbSet<CohortSlotAssignment> CohortSlotAssignments { get; set; }
    public DbSet<CohortMembership> CohortMembership { get; set; }
    public DbSet<ServiceEvaluation> ServiceEvaluation { get; set; }
    public DbSet<ServicePeriod> ServicePeriods { get; set; }
    public DbSet<ServicePeriodSlotCoverage> ServicePeriodSlotCoverage { get; set; }
    public DbSet<Holiday> Holidays { get; set; }

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

    /// <inheritdoc />
    public async Task<Result<T>> ExecuteAtomicallyAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation, CancellationToken cancellationToken = default)
    {
        // Already inside somebody else's unit of work — joining it is what makes nesting harmless.
        if (Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        // ⚠ Through the execution strategy, not straight to BeginTransaction. Aspire's
        // AddNpgsqlDbContext enables retry-on-failure, and a retrying strategy refuses a
        // user-initiated transaction outright ("does not support user-initiated transactions") —
        // which would turn every wrapped handler into a 500 rather than into an atomic one.
        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            // A retry re-runs the operation from the top, so anything the failed attempt tracked has
            // to go: left behind, it would be inserted a second time by the attempt that succeeds.
            ChangeTracker.Clear();

            await using var transaction = await Database.BeginTransactionAsync(ct);

            var result = await operation(ct);

            if (result.IsFailure)
            {
                await transaction.RollbackAsync(ct);
                return result;
            }

            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
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
