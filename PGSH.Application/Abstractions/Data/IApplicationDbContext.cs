using PGSH.Domain.Todos;
using PGSH.Domain.Users;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<TodoItem> TodoItems { get; }
    DbSet<Student> Students { get; set; }
    DbSet<Employee> Employees { get; set; }
    DbSet<Stage> Stages { get; set; }
    DbSet<StageGroup> StagesGroup { get; set; }
    DbSet<InternshipAssignment> InternshipAssignments { get; set; }
    DbSet<AssignmentPeriod> AssignmentPeriods { get; set; }
    DbSet<Center> Centers { get; set; }
    DbSet<Hospital> Hospitals { get; set; }
    DbSet<Service> Services { get; set; }
    DbSet<Registration> Registrations { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
