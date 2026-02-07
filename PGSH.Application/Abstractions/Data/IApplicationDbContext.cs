using PGSH.Domain.Todos;
using PGSH.Domain.Users;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<TodoItem> TodoItems { get; }
    DbSet<Student> Students { get; set; }
    DbSet<Employee> Employees { get; set; }
    DbSet<Stage> Stages { get; set; }
    DbSet<InternshipAssignment> InternshipAssignments { get; set; }
    DbSet<Center> Centers { get; set; }
    DbSet<Hospital> Hospitals { get; set; }
    DbSet<Level> Levels { get; set; }
    DbSet<Service> Services { get; set; }
    DbSet<Registration> Registrations { get; set; }
    DbSet<History> Histories { get; set; }
    DbSet<AttendanceRecord> AttendanceRecords { get; set; }
    DbSet<StageObjective> StageObjectives { get; set; }
    DbSet<ObjectiveScore> ObjectiveScores { get; set; }
    DbSet<Cohort> Cohorts { get; set; }
    DbSet<CohortRotationTemplate> CohortRotationTemplates { get; set; }
    DbSet<CohortMembership> CohortMembership { get; set; }
    DbSet<ServiceEvaluation> ServiceEvaluation { get; set; }
    DbSet<ServicePeriod> ServicePeriods { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
