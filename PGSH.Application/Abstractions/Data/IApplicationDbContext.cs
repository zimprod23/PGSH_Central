using PGSH.Domain.Audit;
using PGSH.Domain.Calendar;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using Microsoft.EntityFrameworkCore;
using PGSH.SharedKernel;

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
    DbSet<FinalYearEntryWaiver> FinalYearEntryWaivers { get; set; }
    DbSet<RegistrationHold> RegistrationHolds { get; set; }
    DbSet<PriorEnrolment> PriorEnrolments { get; set; }
    DbSet<Cohort> Cohorts { get; set; }
    DbSet<CnpnVersion> CnpnVersions { get; set; }
    DbSet<CnpnLevelEffectivity> CnpnLevelEffectivities { get; set; }
    DbSet<Curriculum> Curriculums { get; set; }
    DbSet<CurriculumStage> CurriculumStages { get; set; }
    DbSet<StageSlot> StageSlots { get; set; }
    DbSet<CohortSlotAssignment> CohortSlotAssignments { get; set; }
    DbSet<CohortMembership> CohortMembership { get; set; }
    DbSet<ServiceEvaluation> ServiceEvaluation { get; set; }
    DbSet<ServicePeriod> ServicePeriods { get; set; }
    DbSet<ServicePeriodSlotCoverage> ServicePeriodSlotCoverage { get; set; }
    DbSet<AuditLog> AuditLogs { get; set; }
    DbSet<Holiday> Holidays { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a unit of work made of several saves as <b>one</b> database transaction: it lands whole,
    /// or not at all.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>This is about the browser, not about the database.</b> An orchestrator that saves
    /// once per step — the macro plan writes cohorts, then affectations, then cells, stage by stage —
    /// leaves a half-built plan behind whenever the request stops early, and a request stops early
    /// every time the page is closed or the connection drops: ASP.NET cancels the token, EF abandons
    /// the remaining steps, and the saves already committed stay. What is left then looks exactly
    /// like a plan somebody meant to author.</para>
    /// <para>A refusal rolls back too. The point is that a plan is written whole or not at all, and a
    /// <see cref="Result"/> failure returned halfway through is the same partial state as a dropped
    /// connection.</para>
    /// <para>⚠ Domain events still publish from each inner <c>SaveChangesAsync</c>, i.e. <i>before</i>
    /// the outer commit. A unit of work wrapped here must therefore raise no event whose handler
    /// assumes the write is already durable.</para>
    /// </remarks>
    Task<Result<T>> ExecuteAtomicallyAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation, CancellationToken cancellationToken = default);
}
