using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Forced mid-stage hand-off. When a student is transferred while a stage is already in
/// progress, the normal transfer (cohort pointer + membership only) leaves the running rotation
/// pinned to the old service. This re-routes the in-flight rotation to the target group's
/// services so the new chef can actually supervise and evaluate:
///   • completed periods are left untouched (history, attendance, evaluations preserved);
///   • the period currently in progress is closed at the transfer date and kept as an
///     <see cref="ServicePeriod.IsInterrupted"/> historical record (not re-evaluated);
///   • the remaining window of that period and every still-future period are re-created against
///     the target cohort's slot cells, so they become real, actionable periods for the new chef.
/// Only runs on an assignment that has a started, not-yet-complete period.
/// </summary>
internal sealed class MidStageTransferRescheduler(IApplicationDbContext dbContext)
{
    public async Task<Result> RerouteAsync(
        InternshipAssignment assignment, int targetCohortId, DateOnly date, CancellationToken ct)
    {
        var movable = assignment.ServicePeriods
            .Where(p => !p.IsComplete && !p.IsInterrupted)
            .ToList();

        // Nothing in flight → not a mid-stage case; the plain transfer already covers it.
        if (!movable.Any(p => p.IsStarted))
            return Result.Success();

        var targetSlots = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(sa => sa.CohortId == targetCohortId)
            .Select(sa => new
            {
                sa.Id,
                sa.StageSlotId,
                sa.ServiceId,
                sa.StageSlot.PeriodNumber,
                sa.StageSlot.StartDate,
                sa.StageSlot.EndDate,
            })
            .ToListAsync(ct);

        var slotByStageSlotId = targetSlots.ToDictionary(s => s.StageSlotId);

        // Every period being moved must have a matching cell in the target group's schedule —
        // otherwise the student would land in a void. Fail clearly instead of leaving a gap.
        var missing = movable
            .Where(p => p.CohortSlotAssignmentId is null
                     || !slotByStageSlotId.ContainsKey(p.CohortSlotAssignment!.StageSlotId))
            .Select(p => p.CohortSlotAssignment?.StageSlot.PeriodNumber)
            .Where(n => n is not null)
            .Select(n => n!.Value)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (missing.Count > 0)
            return Result.Failure(StageErrors.TargetScheduleMissingPeriods(targetCohortId, missing));

        foreach (var period in movable)
        {
            var target = slotByStageSlotId[period.CohortSlotAssignment!.StageSlotId];

            if (period.IsStarted)
            {
                // Cut the running rotation at the transfer date; keep it for history.
                period.IsInterrupted = true;
                period.EndDate = date < period.EndDate ? date : period.EndDate;

                assignment.ServicePeriods.Add(new ServicePeriod
                {
                    InternshipAssignmentId = assignment.Id,
                    ServiceId              = target.ServiceId,
                    CohortSlotAssignmentId = target.Id,
                    StartDate              = date < target.EndDate ? date : target.StartDate,
                    EndDate                = target.EndDate,
                    IsStarted              = true,
                });
            }
            else
            {
                // Future period: drop the old one (no attendance/eval) and re-create it whole
                // against the target service, inactive until started in the normal flow.
                assignment.ServicePeriods.Remove(period);

                assignment.ServicePeriods.Add(new ServicePeriod
                {
                    InternshipAssignmentId = assignment.Id,
                    ServiceId              = target.ServiceId,
                    CohortSlotAssignmentId = target.Id,
                    StartDate              = target.StartDate,
                    EndDate                = target.EndDate,
                    IsStarted              = false,
                });
            }
        }

        return Result.Success();
    }
}
