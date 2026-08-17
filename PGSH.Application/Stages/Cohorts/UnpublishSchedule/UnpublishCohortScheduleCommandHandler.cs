using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.UnpublishSchedule;

/// <summary>
/// The inverse of publishing: removes the execution records the planning grid produced, and nothing
/// else.
///
/// <para>⚠ <b>Deleting a <see cref="ServicePeriod"/> is not a bookkeeping act.</b>
/// <c>ServiceEvaluation</c>, <c>AttendanceRecord</c>, <c>PeriodPause</c> and <c>Delocalization</c>
/// all cascade from it. So the marks a chef entered and every day of attendance recorded against a
/// rotation disappear with it, silently and unrecoverably. Once anything has started this is no
/// longer an undo, and the caller is told exactly what it would cost before being allowed to
/// insist.</para>
/// </summary>
internal sealed class UnpublishCohortScheduleCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UnpublishCohortScheduleCommand, UnpublishResult>
{
    public async Task<Result<UnpublishResult>> Handle(
        UnpublishCohortScheduleCommand request, CancellationToken cancellationToken)
    {
        bool cohortExists = await dbContext.Cohorts
            .AnyAsync(c => c.Id == request.CohortId, cancellationToken);
        if (!cohortExists)
            return Result.Failure<UnpublishResult>(StageErrors.CohortNotFound(request.CohortId));

        var assignmentIds = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var toll = await dbContext.ServicePeriods
            .Where(p => assignmentIds.Contains(p.InternshipAssignmentId) && p.CohortSlotAssignmentId != null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Periods    = g.Count(),
                Started    = g.Count(p => p.IsStarted),
                Evaluated  = g.Count(p => p.Evaluation != null),
                Attendance = g.Sum(p => p.Attendance.Count),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (toll is null)
            return Result.Failure<UnpublishResult>(StageErrors.ScheduleNotPublished);

        bool underway = toll.Started > 0 || toll.Evaluated > 0 || toll.Attendance > 0;
        if (underway && !request.Force)
            return Result.Failure<UnpublishResult>(StageErrors.ScheduleUnderway(
                toll.Periods, toll.Started, toll.Evaluated, toll.Attendance));

        // Tracked with their periods: removal goes through the aggregate so the status and the note
        // are recomputed from what is left. Deleting the rows underneath the aggregate is what left
        // assignments reading "Validated, 14.5" with nothing behind them.
        var assignments = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.Evaluation)
            .ToListAsync(cancellationToken);

        int removed = assignments.Sum(a => a.RemovePublishedPeriods());
        int adHocKept = assignments.Sum(a => a.ServicePeriods.Count);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UnpublishResult(removed, toll.Evaluated, toll.Attendance, adHocKept);
    }
}
