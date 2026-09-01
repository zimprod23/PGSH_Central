using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Delete;

/// <summary>
/// Deletes one cohorte and everything built on it.
/// </summary>
/// <remarks>
/// <para>⚠ <b>This path had no guard at all</b>, while its bulk twin
/// (<c>DeleteAllCohortsCommandHandler</c>) refuses as soon as one affectation has left
/// <see cref="InternshipStatus.Planned"/>. So the safe act was the one that touched a hundred
/// cohortes and the unguarded one was the single-row button beside each line: deleting a cohorte
/// mid-stage removed every <c>ServicePeriod</c> — « plan-generated and ad-hoc » alike — and
/// <c>ServiceEvaluation</c>, <c>AttendanceRecord</c>, <c>PeriodPause</c> and <c>Delocalization</c>
/// all cascade from those. A chef's marks and a term of attendance, gone on one click, with a 204 and
/// no number.</para>
///
/// <para>Now the two agree, and both name what they removed.</para>
/// </remarks>
internal sealed class DeleteCohortCommandHandler(
    IApplicationDbContext dbContext,
    AffectationTollReader tollReader)
    : ICommandHandler<DeleteCohortCommand, DeleteCohortResult>
{
    public async Task<Result<DeleteCohortResult>> Handle(
        DeleteCohortCommand request, CancellationToken cancellationToken)
    {
        var cohort = await dbContext.Cohorts
            .FirstOrDefaultAsync(c => c.Id == request.CohortId, cancellationToken);

        if (cohort is null)
            return Result.Failure<DeleteCohortResult>(StageErrors.CohortNotFound(request.CohortId));

        var toll = await tollReader.ForCohortsAsync([request.CohortId], cancellationToken);

        if (toll.IsUnderway)
            return Result.Failure<DeleteCohortResult>(StageErrors.CohortAffectationsUnderway(
                cohort.Label, toll.Assignments, toll.Periods,
                toll.Started, toll.Evaluated, toll.AttendanceDays));

        var assignments = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .Include(a => a.ServicePeriods)
            .ToListAsync(cancellationToken);

        int periodsRemoved = assignments.Sum(a => a.ServicePeriods.Count);

        foreach (var assignment in assignments)
            dbContext.ServicePeriods.RemoveRange(assignment.ServicePeriods);

        // MembershipHistory cascades with the assignment; these are the records pointing *into* this
        // cohorte from assignments that live elsewhere — the trace of a transfer.
        var visitingMemberships = await dbContext.CohortMembership
            .Where(m => m.CohortId == request.CohortId)
            .ToListAsync(cancellationToken);

        dbContext.CohortMembership.RemoveRange(visitingMemberships);
        dbContext.InternshipAssignments.RemoveRange(assignments);
        dbContext.Cohorts.Remove(cohort);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteCohortResult(assignments.Count, periodsRemoved);
    }
}
