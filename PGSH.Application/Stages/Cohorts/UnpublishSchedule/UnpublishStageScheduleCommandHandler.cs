using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.UnpublishSchedule;

/// <summary>
/// Undoes a stage's publication in one act: three flat reads, one write, one cache invalidation —
/// against the client-side loop's <i>N</i> round trips, <i>N</i> refetches of the page and up to
/// <i>N</i> red toasts.
///
/// <para>⚠ <b>What a cohorte being « underway » means is read exactly as the per-cohorte command
/// reads it</b> — a started période, a mark, or a day of attendance. Two acts that destroy the same
/// rows must not disagree about which rows they are, and a bulk sweep that were <i>stricter</i>
/// would skip cohortes the single button undoes without complaint.</para>
/// </summary>
internal sealed class UnpublishStageScheduleCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : ICommandHandler<UnpublishStageScheduleCommand, UnpublishStageResult>
{
    public async Task<Result<UnpublishStageResult>> Handle(
        UnpublishStageScheduleCommand request, CancellationToken cancellationToken)
    {
        bool stageExists = await dbContext.Stages
            .AnyAsync(s => s.Id == request.StageId, cancellationToken);

        if (!stageExists)
            return Result.Failure<UnpublishStageResult>(StageErrors.NotFound(request.StageId));

        // ⚠ Resolved, never left null. Unscoped, "unpublish this stage" would reach the six years it
        // ever ran — 563 cohortes on the worst stage — on the one act here that deletes rows.
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<UnpublishStageResult>(year.Error);

        var cohortIds = await SchedulePublisher
            .CohortIdsQuery(dbContext, request.StageId, year.Value, request.PartitionLabels)
            .ToListAsync(cancellationToken);

        if (cohortIds.Count == 0)
            return Result.Success(new UnpublishStageResult(0, 0, 0, 0, 0, 0, 0, []));

        var tolls = await CohortTollsQuery(dbContext, cohortIds).ToListAsync(cancellationToken);

        var underway = tolls.Where(t => t.Started > 0 || t.Evaluations > 0 || t.AttendanceDays > 0).ToList();

        var underwayIds = underway.Select(t => t.CohortId).ToHashSet();
        var undoable = tolls.Where(t => !underwayIds.Contains(t.CohortId)).Select(t => t.CohortId).ToList();

        // ⚠ One Include for the whole stage rather than one per cohorte. The graph is large — the
        // 3ᵉ MED is ~940 affectations carrying ~5 600 périodes — but it is a single round trip, and
        // the aggregate has to hold the périodes to recompute the note and the status from what is
        // left. Deleting the rows underneath it is what left assignments reading « Validated, 14.5 »
        // with nothing behind them.
        var assignments = undoable.Count == 0
            ? []
            : await dbContext.InternshipAssignments
                .Where(a => undoable.Contains(a.CurrentCohortId))
                .Include(a => a.ServicePeriods)
                    .ThenInclude(p => p.Evaluation)
                .ToListAsync(cancellationToken);

        int removed = assignments.Sum(a => a.RemovePublishedPeriods());
        int adHocKept = assignments.Sum(a => a.ServicePeriods.Count);

        if (removed > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        var heaviest = underway
            .OrderByDescending(t => t.Evaluations)
            .ThenByDescending(t => t.AttendanceDays)
            .ThenByDescending(t => t.Started)
            .Take(UnpublishStageResult.MaxReportedSkipped)
            .Select(t => new SkippedCohort(
                t.CohortId, t.Label, t.Periods, t.Started, t.Evaluations, t.AttendanceDays))
            .ToList();

        return Result.Success(new UnpublishStageResult(
            // Every cohorte in `undoable` came out of the toll query, so it held at least one
            // grid-linked période — there is nothing to re-derive from the assignments.
            CohortsUnpublished:     undoable.Count,
            PeriodsRemoved:         removed,
            AdHocPeriodsKept:       adHocKept,
            CohortsSkippedUnderway: underway.Count,
            PeriodsUnderway:        underway.Sum(t => t.Periods),
            EvaluationsAtRisk:      underway.Sum(t => t.Evaluations),
            AttendanceDaysAtRisk:   underway.Sum(t => t.AttendanceDays),
            HeaviestSkipped:        heaviest));
    }

    /// <summary>
    /// One row per cohorte that holds at least one <b>grid-linked</b> période, with the four counts
    /// <c>StageErrors.ScheduleUnderway</c> names.
    /// </summary>
    /// <remarks>
    /// ⚠ Grouped over the <b>périodes</b>, not over the cohortes: folding an aggregate over a
    /// collection navigation inside a second aggregate is the shape Npgsql refuses — the family that
    /// killed the macro plan. A cohorte with no grid-linked période simply produces no row, which is
    /// also what makes it fall out of both the undoable and the skipped counts. Pinned by
    /// <c>SqlTranslationTests</c>.
    /// </remarks>
    internal static IQueryable<CohortToll> CohortTollsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> cohortIds) =>
        dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => p.CohortSlotAssignmentId != null
                     && cohortIds.Contains(p.InternshipAssignment.CurrentCohortId))
            .GroupBy(p => new
            {
                p.InternshipAssignment.CurrentCohortId,
                p.InternshipAssignment.Cohort.Label,
            })
            .Select(g => new CohortToll(
                g.Key.CurrentCohortId,
                g.Key.Label,
                g.Count(),
                g.Count(p => p.IsStarted),
                g.Count(p => p.Evaluation != null),
                g.Sum(p => p.Attendance.Count)));

    internal sealed record CohortToll(
        int CohortId, string Label, int Periods, int Started, int Evaluations, int AttendanceDays);
}
