using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Stage-scoped lifecycle ops (start / complete service periods) executed in a SINGLE
/// round-trip, narrowed to one academic year and optionally to explicit cohorts, a partition
/// (RotationGroup) and/or a window of periods. Mirrors
/// <see cref="SchedulePublisher.PublishStageAsync"/>: load every in-scope assignment once, mutate
/// the relevant <see cref="ServicePeriod"/>s, persist once. Re-running on an already-started/closed
/// selection loads once and finds nothing to do — a cheap near no-op, not N wasteful per-cohort
/// writes.
///
/// The year is mandatory, not optional. A stage keeps a cohort per (group, year), so "démarrer
/// Chirurgie" without it would start the rotations of every promotion that ever took the stage.
/// </summary>
internal sealed class StagePeriodRunner(IApplicationDbContext dbContext)
{
    public Task<Result<int>> StartStageAsync(
        int stageId, int academicYearId, IReadOnlyList<int>? cohortIds,
        IReadOnlyList<string>? partitionLabels, IReadOnlyList<int>? periodNumbers, CancellationToken ct) =>
        RunAsync(stageId, academicYearId, cohortIds, partitionLabels, periodNumbers,
            statusFilter: a => a.Status == InternshipStatus.Planned || a.Status == InternshipStatus.Ongoing,
            isPending:    p => !p.IsStarted,
            apply:        (a, periodId) => a.StartPeriod(periodId),
            ct);

    public Task<Result<int>> CompleteStageAsync(
        int stageId, int academicYearId, IReadOnlyList<int>? cohortIds,
        IReadOnlyList<string>? partitionLabels, IReadOnlyList<int>? periodNumbers, CancellationToken ct) =>
        RunAsync(stageId, academicYearId, cohortIds, partitionLabels, periodNumbers,
            statusFilter: a => a.Status == InternshipStatus.Ongoing,
            isPending:    p => !p.IsComplete,
            apply:        (a, periodId) => a.CompletePeriod(periodId),
            ct);

    private async Task<Result<int>> RunAsync(
        int stageId,
        int academicYearId,
        IReadOnlyList<int>? cohortIds,
        IReadOnlyList<string>? partitionLabels,
        IReadOnlyList<int>? periodNumbers,
        Expression<Func<InternshipAssignment, bool>> statusFilter,
        Func<ServicePeriod, bool> isPending,
        Func<InternshipAssignment, Guid, Result> apply,
        CancellationToken ct)
    {
        var query = dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.CohortSlotAssignment)
                    .ThenInclude(sa => sa!.StageSlot)
            // Loaded so CompletePeriod can auto-revert a temporary transfer at stage end.
            .Include(a => a.MembershipHistory)
            .Where(a => a.Cohort.StageId == stageId)
            .Where(a => a.Cohort.AcademicGroup.AcademicYearId == academicYearId)
            .Where(statusFilter);

        // Explicit cohorts take precedence (the Suivi UI selects arbitrary cohorts); the partition
        // label path mirrors the macro plan / stage publish scoping for whole-rotation operations.
        if (cohortIds is { Count: > 0 })
            query = query.Where(a => cohortIds.Contains(a.CurrentCohortId));
        else if (partitionLabels is { Count: > 0 })
            query = query.Where(a => a.Cohort.AcademicGroup.RotationGroup != null
                                  && partitionLabels.Contains(a.Cohort.AcademicGroup.RotationGroup));

        var assignments = await query.ToListAsync(ct);

        var scopedPeriods = periodNumbers is { Count: > 0 } ? periodNumbers.ToHashSet() : null;

        int affected = 0;
        foreach (var a in assignments)
            foreach (var period in a.ServicePeriods.Where(isPending).ToList())
            {
                if (scopedPeriods is not null
                    && (period.CohortSlotAssignment is null
                        || !scopedPeriods.Contains(period.CohortSlotAssignment.StageSlot.PeriodNumber)))
                    continue;

                if (apply(a, period.Id).IsSuccess) affected++;
            }

        if (affected > 0)
            await dbContext.SaveChangesAsync(ct);

        return Result.Success(affected);
    }
}
