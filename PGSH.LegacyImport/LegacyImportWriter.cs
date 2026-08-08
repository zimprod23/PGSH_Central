using Microsoft.EntityFrameworkCore;
using PGSH.Infrastructure.Database;
using PGSH.LegacyImport.Mapping;

namespace PGSH.LegacyImport;

/// <summary>
/// Commits a <see cref="LegacyImportPlan"/>, in dependency order and in batches.
///
/// The batching is not just for speed. EF's change tracker degrades badly past a few tens of thousands
/// of tracked entities, and this plan holds well over 200,000. Reference data therefore goes in first
/// so its identity keys exist; the assignments are then detached from their navigation properties,
/// pinned to those keys directly, and flushed in chunks with the tracker cleared between them.
/// </summary>
internal static class LegacyImportWriter
{
    private const int BatchSize = 2_000;

    public static async Task WriteAsync(ApplicationDbContext db, LegacyImportPlan plan, TextWriter log)
    {
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        db.Centers.Add(plan.Center);
        db.Hospitals.AddRange(plan.Hospitals);
        db.Services.AddRange(plan.Services);
        db.Levels.AddRange(plan.Levels);
        db.Stages.AddRange(plan.Stages);
        db.AcademicYears.AddRange(plan.AcademicYears);
        db.AcademicGroups.AddRange(plan.AcademicGroups);
        await db.SaveChangesAsync();
        log.WriteLine($"  reference data … {plan.Services.Count:N0} services, {plan.Stages.Count:N0} stages, {plan.AcademicGroups.Count:N0} groups");

        db.Students.AddRange(plan.Students);
        await db.SaveChangesAsync();
        log.WriteLine($"  students … {plan.Students.Count:N0}");

        db.Registrations.AddRange(plan.Registrations);
        await db.SaveChangesAsync();
        log.WriteLine($"  registrations … {plan.Registrations.Count:N0}");

        db.Cohorts.AddRange(plan.Cohorts);
        await db.SaveChangesAsync();
        log.WriteLine($"  cohorts … {plan.Cohorts.Count:N0}");

        // Everything above now has its generated key. Copy those onto the assignments and drop the
        // object references, so a cleared tracker cannot mistake an already-saved parent for a new one.
        foreach (var assignment in plan.Assignments)
        {
            assignment.RegistrationId = assignment.Registration.Id;
            assignment.CurrentCohortId = assignment.Cohort.Id;
            assignment.Registration = null!;
            assignment.Cohort = null!;

            foreach (var period in assignment.ServicePeriods)
            {
                period.ServiceId = period.Service.Id;
                period.Service = null!;
            }

            foreach (var membership in assignment.MembershipHistory)
            {
                membership.CohortId = membership.Cohort.Id;
                membership.Cohort = null!;
            }
        }

        db.ChangeTracker.Clear();

        int written = 0;
        foreach (var batch in plan.Assignments.Chunk(BatchSize))
        {
            db.InternshipAssignments.AddRange(batch);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            written += batch.Length;
            log.WriteLine($"  assignments … {written:N0} / {plan.Assignments.Count:N0}");
        }
    }
}
