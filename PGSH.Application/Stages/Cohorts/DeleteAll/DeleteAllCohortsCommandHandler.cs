using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.DeleteAll;

/// <summary>
/// Resets one stage's cohortes for one year.
/// </summary>
/// <remarks>
/// <para>The guard is the same as the single-cohorte delete's, read through the same
/// <see cref="AffectationTollReader"/> so the two refusals cannot describe the same rows
/// differently — and it now <b>names</b> what it is refusing over. « des affectations sont déjà en
/// cours » told the admin nothing about which stage, how far along, or what to do next.</para>
/// </remarks>
internal sealed class DeleteAllCohortsCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    AffectationTollReader tollReader)
    : ICommandHandler<DeleteAllCohortsCommand, DeleteAllCohortsResult>
{
    public async Task<Result<DeleteAllCohortsResult>> Handle(
        DeleteAllCohortsCommand request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<DeleteAllCohortsResult>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        var stage = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == request.StageId)
            .Select(s => new { s.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (stage is null)
            return Result.Failure<DeleteAllCohortsResult>(StageErrors.NotFound(request.StageId));

        var cohortIds = await dbContext.Cohorts
            .Where(c => c.StageId == request.StageId && c.AcademicGroup.AcademicYearId == yearId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (cohortIds.Count == 0)
            return new DeleteAllCohortsResult(0, 0, 0);

        var toll = await tollReader.ForCohortsAsync(cohortIds, cancellationToken);

        if (toll.IsUnderway)
        {
            return Result.Failure<DeleteAllCohortsResult>(StageErrors.StageCohortsUnderway(
                stage.Name, yearLabel, cohortIds.Count, toll.Assignments, toll.Periods,
                toll.Started, toll.Evaluated, toll.AttendanceDays));
        }

        var assignmentIds = await dbContext.InternshipAssignments
            .Where(a => cohortIds.Contains(a.CurrentCohortId))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        int periodsRemoved = 0;

        if (assignmentIds.Count > 0)
        {
            periodsRemoved = await dbContext.ServicePeriods
                .Where(p => assignmentIds.Contains(p.InternshipAssignmentId))
                .ExecuteDeleteAsync(cancellationToken);

            // Memberships belonging to these assignments (the cascade path) and those pointing into
            // these cohortes from assignments elsewhere (the transfer path).
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

        int deleted = await dbContext.Cohorts
            .Where(c => cohortIds.Contains(c.Id))
            .ExecuteDeleteAsync(cancellationToken);

        return new DeleteAllCohortsResult(deleted, assignmentIds.Count, periodsRemoved);
    }
}
