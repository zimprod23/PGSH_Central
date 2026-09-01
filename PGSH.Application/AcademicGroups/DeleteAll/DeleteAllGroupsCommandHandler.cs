using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.DeleteAll;

/// <summary>
/// Removes every roster of a year, and the cohortes hanging off them.
/// </summary>
/// <remarks>
/// The order it enforces — empty the rosters, then delete them — is what keeps this safe: a roster
/// can only be emptied once its affectations are gone or explicitly dropped, so by the time this runs
/// there is normally nothing left to destroy. The guard stays for the rosters emptied before that
/// rule existed, which left their affectations behind: this is the act that would sweep them away.
/// </remarks>
internal sealed class DeleteAllGroupsCommandHandler(
    IApplicationDbContext dbContext,
    AffectationTollReader tollReader)
    : ICommandHandler<DeleteAllGroupsCommand, int>
{
    public async Task<Result<int>> Handle(DeleteAllGroupsCommand request, CancellationToken cancellationToken)
    {
        var groupIds = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        if (groupIds.Count == 0)
            return Result.Success(0);

        bool hasStudents = await dbContext.Registrations
            .AnyAsync(r => r.AcademicGroupId != null && groupIds.Contains(r.AcademicGroupId.Value), cancellationToken);

        if (hasStudents)
            return Result.Failure<int>(Error.Conflict(
                "AcademicGroups.HasStudents",
                "One or more groups in this year have students assigned. Empty all groups before deleting them."));

        var cohortIds = await dbContext.Cohorts
            .Where(c => groupIds.Contains(c.AcademicGroupId))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (cohortIds.Count > 0)
        {
            var toll = await tollReader.ForCohortsAsync(cohortIds, cancellationToken);

            if (toll.IsUnderway)
            {
                string yearLabel = await dbContext.AcademicYears
                    .Where(y => y.Id == request.AcademicYearId)
                    .Select(y => y.Label)
                    .FirstOrDefaultAsync(cancellationToken) ?? $"L'année {request.AcademicYearId}";

                return Result.Failure<int>(AcademicGroupErrors.YearRostersUnderway(
                    yearLabel, cohortIds.Count, toll.Assignments, toll.Periods,
                    toll.Started, toll.Evaluated, toll.AttendanceDays));
            }

            var assignmentIds = await dbContext.InternshipAssignments
                .Where(a => cohortIds.Contains(a.CurrentCohortId))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            if (assignmentIds.Count > 0)
            {
                await dbContext.ServicePeriods
                    .Where(p => assignmentIds.Contains(p.InternshipAssignmentId))
                    .ExecuteDeleteAsync(cancellationToken);

                await dbContext.CohortMembership
                    .Where(m => assignmentIds.Contains(m.InternshipAssignmentId)
                             || cohortIds.Contains(m.CohortId))
                    .ExecuteDeleteAsync(cancellationToken);

                await dbContext.InternshipAssignments
                    .Where(a => cohortIds.Contains(a.CurrentCohortId))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await dbContext.CohortSlotAssignments
                .Where(a => cohortIds.Contains(a.CohortId))
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.Cohorts
                .Where(c => groupIds.Contains(c.AcademicGroupId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        int deleted = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .ExecuteDeleteAsync(cancellationToken);

        return Result.Success(deleted);
    }
}
