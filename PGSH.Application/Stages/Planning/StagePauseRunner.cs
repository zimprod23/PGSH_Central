using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Stage-scoped pause / resume of service periods (e.g. an exam week mid-rotation) in a SINGLE
/// round-trip, narrowed to one academic year and optionally to explicit cohorts, a partition
/// (RotationGroup) and/or a window of periods. Mirrors <see cref="StagePeriodRunner"/>: load every
/// in-scope assignment once, mutate the relevant <see cref="ServicePeriod"/>s, persist once.
/// Idempotent — re-running on an already-paused/resumed selection finds nothing pending.
///
/// As with the period runner the year is mandatory: pausing "Chirurgie" for an exam week must not
/// reach into the rotations of a promotion that sat those exams years ago.
/// </summary>
internal sealed class StagePauseRunner(IApplicationDbContext dbContext)
{
    public Task<Result<int>> PauseStageAsync(
        int stageId, int academicYearId, IReadOnlyList<int>? cohortIds,
        IReadOnlyList<string>? partitionLabels, IReadOnlyList<int>? periodNumbers,
        PauseKind kind, string? reason, CancellationToken ct) =>
        RunAsync(stageId, academicYearId, cohortIds, partitionLabels, periodNumbers,
            isPending: p => p.IsStarted && !p.IsComplete && !p.IsInterrupted && !p.IsPaused,
            apply:     (a, periodId, date) => a.PausePeriod(periodId, date, kind, reason),
            ct);

    public Task<Result<int>> ResumeStageAsync(
        int stageId, int academicYearId, IReadOnlyList<int>? cohortIds,
        IReadOnlyList<string>? partitionLabels, IReadOnlyList<int>? periodNumbers, CancellationToken ct) =>
        RunAsync(stageId, academicYearId, cohortIds, partitionLabels, periodNumbers,
            isPending: p => p.IsPaused,
            apply:     (a, periodId, date) => a.ResumePeriod(periodId, date),
            ct);

    private async Task<Result<int>> RunAsync(
        int stageId,
        int academicYearId,
        IReadOnlyList<int>? cohortIds,
        IReadOnlyList<string>? partitionLabels,
        IReadOnlyList<int>? periodNumbers,
        Func<ServicePeriod, bool> isPending,
        Func<InternshipAssignment, Guid, DateOnly, Result> apply,
        CancellationToken ct)
    {
        var query = dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.CohortSlotAssignment)
                    .ThenInclude(sa => sa!.StageSlot)
            // Loaded so resume can close the open pause window and shift the rotation forward.
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.Pauses)
            .Where(a => a.Cohort.StageId == stageId)
            .Where(a => a.Cohort.AcademicGroup.AcademicYearId == academicYearId)
            .Where(a => a.Status == InternshipStatus.Ongoing);

        if (cohortIds is { Count: > 0 })
            query = query.Where(a => cohortIds.Contains(a.CurrentCohortId));
        else if (partitionLabels is { Count: > 0 })
            query = query.Where(a => a.Cohort.AcademicGroup.RotationGroup != null
                                  && partitionLabels.Contains(a.Cohort.AcademicGroup.RotationGroup));

        var assignments = await query.ToListAsync(ct);

        var scopedPeriods = periodNumbers is { Count: > 0 } ? periodNumbers.ToHashSet() : null;
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        int affected = 0;
        foreach (var a in assignments)
            foreach (var period in a.ServicePeriods.Where(isPending).ToList())
            {
                if (scopedPeriods is not null
                    && (period.CohortSlotAssignment is null
                        || !scopedPeriods.Contains(period.CohortSlotAssignment.StageSlot.PeriodNumber)))
                    continue;

                if (apply(a, period.Id, date).IsSuccess) affected++;
            }

        if (affected > 0)
            await dbContext.SaveChangesAsync(ct);

        return Result.Success(affected);
    }
}
