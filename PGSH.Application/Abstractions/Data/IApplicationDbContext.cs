using PGSH.Domain.Audit;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace PGSH.Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Student> Students { get; set; }
    DbSet<Employee> Employees { get; set; }
    DbSet<Stage> Stages { get; set; }
    DbSet<InternshipAssignment> InternshipAssignments { get; set; }
    DbSet<Center> Centers { get; set; }
    DbSet<Hospital> Hospitals { get; set; }
    DbSet<Level> Levels { get; set; }
    DbSet<Service> Services { get; set; }
    DbSet<ServiceLevelCapacity> ServiceLevelCapacities { get; set; }
    DbSet<Registration> Registrations { get; set; }
    DbSet<History> Histories { get; set; }
    DbSet<AttendanceRecord> AttendanceRecords { get; set; }
    DbSet<StageObjective> StageObjectives { get; set; }
    DbSet<ObjectiveScore> ObjectiveScores { get; set; }
    DbSet<AcademicYear> AcademicYears { get; set; }
    DbSet<AcademicGroup> AcademicGroups { get; set; }
    DbSet<Cohort> Cohorts { get; set; }
    DbSet<CnpnVersion> CnpnVersions { get; set; }
    DbSet<Curriculum> Curriculums { get; set; }
    DbSet<CurriculumStage> CurriculumStages { get; set; }
    DbSet<StageSlot> StageSlots { get; set; }
    DbSet<CohortSlotAssignment> CohortSlotAssignments { get; set; }
    DbSet<CohortMembership> CohortMembership { get; set; }
    DbSet<ServiceEvaluation> ServiceEvaluation { get; set; }
    DbSet<ServicePeriod> ServicePeriods { get; set; }
    DbSet<AuditLog> AuditLogs { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
