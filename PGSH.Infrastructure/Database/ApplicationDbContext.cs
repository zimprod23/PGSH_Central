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

namespace PGSH.Infrastructure.Database;

public sealed class ApplicationDbContext
    : DbContext, IApplicationDbContext
{
    private readonly IPublisher? _publisher;
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options,IPublisher? publisher = null): base(options) => _publisher = publisher;
    public DbSet<User> Users { get; set; }

    public DbSet<TodoItem> TodoItems { get; set; }
    public DbSet<Student> Students { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<Stage> Stages { get; set; }
    public DbSet<StageGroup> StagesGroup { get; set; }
    public DbSet<InternshipAssignment> InternshipAssignments { get; set; }
    public DbSet<AssignmentPeriod> AssignmentPeriods { get; set; }
    public DbSet<Center> Centers { get; set; }
    public DbSet<Hospital> Hospitals { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<Registration> Registrations { get; set; }


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
        if (_publisher != null) return;
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
