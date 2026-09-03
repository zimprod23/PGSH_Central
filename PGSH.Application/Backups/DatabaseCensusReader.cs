using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Backups;

namespace PGSH.Application.Backups;

/// <summary>
/// Counts the tables a manifest records. Twelve <c>COUNT(*)</c>s — cheap next to the dump they
/// accompany, and they are what turns « la restauration effacerait des données » into a number.
/// </summary>
/// <remarks>
/// ⚠ The names are <see cref="DatabaseCensus.Tables"/>' and nothing else may invent one: a manifest
/// written with a key no reader looks for is a count nobody will ever compare.
/// </remarks>
public sealed class DatabaseCensusReader(IApplicationDbContext dbContext)
{
    public async Task<DatabaseCensus> ReadAsync(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, long>
        {
            ["Students"] = await dbContext.Students.CountAsync(cancellationToken),
            ["Registrations"] = await dbContext.Registrations.CountAsync(cancellationToken),
            ["InternshipAssignments"] = await dbContext.InternshipAssignments.CountAsync(cancellationToken),
            ["ServicePeriods"] = await dbContext.ServicePeriods.CountAsync(cancellationToken),
            ["ServiceEvaluations"] = await dbContext.ServiceEvaluation.CountAsync(cancellationToken),
            ["AcademicGroups"] = await dbContext.AcademicGroups.CountAsync(cancellationToken),
            ["Cohorts"] = await dbContext.Cohorts.CountAsync(cancellationToken),
            ["StageSlots"] = await dbContext.StageSlots.CountAsync(cancellationToken),
            ["CohortSlotAssignments"] = await dbContext.CohortSlotAssignments.CountAsync(cancellationToken),
            ["RegistrationHolds"] = await dbContext.RegistrationHolds.CountAsync(cancellationToken),
            ["Holidays"] = await dbContext.Holidays.CountAsync(cancellationToken),
            ["AuditLogs"] = await dbContext.AuditLogs.CountAsync(cancellationToken),
        };

        return new DatabaseCensus(counts);
    }
}
