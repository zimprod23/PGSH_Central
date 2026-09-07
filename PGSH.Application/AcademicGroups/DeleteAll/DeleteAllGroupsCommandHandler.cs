using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.DeleteAll;

/// <summary>
/// Removes the rosters in scope, and the cohortes hanging off them.
/// </summary>
/// <remarks>
/// <para>The order it enforces — empty the rosters, then delete them — is what keeps this safe: a
/// roster can only be emptied once its affectations are gone or explicitly dropped, so by the time
/// this runs there is normally nothing left to destroy. The guard stays for the rosters emptied
/// before that rule existed, which left their affectations behind: this is the act that would sweep
/// them away.</para>
///
/// <para>⚠ <b>Every read and every write here is scoped by <c>groupIds</c>, never re-derived from
/// the year.</b> That is the one thing that made the promotion scope a two-line change rather than a
/// rewrite — and the final <c>ExecuteDelete</c> was the exception that had to be corrected, because
/// re-filtering on the year alone would have deleted every promotion's rosters after a refusal check
/// that had only looked at one.</para>
/// </remarks>
internal sealed class DeleteAllGroupsCommandHandler(
    IApplicationDbContext dbContext,
    AffectationTollReader tollReader)
    : ICommandHandler<DeleteAllGroupsCommand, int>
{
    public async Task<Result<int>> Handle(DeleteAllGroupsCommand request, CancellationToken cancellationToken)
    {
        int? levelId = request.LevelId;
        string? levelLabel = null;

        if (levelId is not null)
        {
            // ⚠ An unknown level refuses rather than falling through to « no level named ». That is
            // the widening-on-absence defect, on an act that otherwise writes across a whole year.
            levelLabel = await dbContext.Levels
                .Where(l => l.Id == levelId)
                .Select(l => l.Label)
                .FirstOrDefaultAsync(cancellationToken);

            if (levelLabel is null)
                return Result.Failure<int>(LevelErrors.NotFound(levelId.Value));
        }

        var groupIds = await RosterScope.Query(dbContext, request.AcademicYearId, levelId)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        if (groupIds.Count == 0)
            return Result.Success(0);

        int students = await dbContext.Registrations
            .CountAsync(
                r => r.AcademicGroupId != null && groupIds.Contains(r.AcademicGroupId.Value),
                cancellationToken);

        if (students > 0)
            return Result.Failure<int>(AcademicGroupErrors.RostersHaveStudents(
                await ScopeLabelAsync(request.AcademicYearId, levelLabel, cancellationToken),
                students,
                groupIds.Count));

        var cohortIds = await dbContext.Cohorts
            .Where(c => groupIds.Contains(c.AcademicGroupId))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (cohortIds.Count > 0)
        {
            var toll = await tollReader.ForCohortsAsync(cohortIds, cancellationToken);

            if (toll.IsUnderway)
            {
                string yearLabel = await YearLabelAsync(request.AcademicYearId, cancellationToken);

                return Result.Failure<int>(levelId is null
                    ? AcademicGroupErrors.YearRostersUnderway(
                        yearLabel, cohortIds.Count, toll.Assignments, toll.Periods,
                        toll.Started, toll.Evaluated, toll.AttendanceDays)
                    : AcademicGroupErrors.PromotionRostersUnderway(
                        levelLabel!, yearLabel, cohortIds.Count, toll.Assignments, toll.Periods,
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
            .Where(g => groupIds.Contains(g.Id))
            .ExecuteDeleteAsync(cancellationToken);

        return Result.Success(deleted);
    }

    /// <summary>What the refusal calls the set it is refusing over — the promotion, or the year.</summary>
    private async Task<string> ScopeLabelAsync(
        int academicYearId, string? levelLabel, CancellationToken cancellationToken)
    {
        string yearLabel = await YearLabelAsync(academicYearId, cancellationToken);
        return levelLabel is null ? yearLabel : $"{levelLabel} ({yearLabel})";
    }

    private async Task<string> YearLabelAsync(int academicYearId, CancellationToken cancellationToken) =>
        await dbContext.AcademicYears
            .Where(y => y.Id == academicYearId)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken) ?? $"l'année {academicYearId}";
}
