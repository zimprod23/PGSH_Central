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

        // An ad-hoc rotation carries no schedule cell (a délocalisation, or a revalidation placed by
        // hand), so there is nothing to map onto the target group's grid. Refuse rather than
        // dereference a slot that is not there — the old guard let these through, because a null cell
        // has no period number to report, and the loop below then dereferenced it and threw.
        if (movable.Any(p => p.CohortSlotAssignmentId is null))
            return Result.Failure(StageErrors.CannotRerouteAdHocPeriod);

        // Every period being moved must have a matching cell in the target group's schedule —
        // otherwise the student would land in a void. Fail clearly instead of leaving a gap.
        var missing = movable
            .Where(p => !slotByStageSlotId.ContainsKey(p.CohortSlotAssignment!.StageSlotId))
            .Select(p => p.CohortSlotAssignment!.StageSlot.PeriodNumber)
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
                CutShortAt(period, date);

                assignment.ServicePeriods.Add(new ServicePeriod
                {
                    InternshipAssignmentId = assignment.Id,
                    ServiceId              = target.ServiceId,
                    CohortSlotAssignmentId = target.Id,
                    StartDate              = RemainingWindowStart(date, target.StartDate, target.EndDate),
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

    /// <summary>
    /// Ends a rotation on the transfer date, never before it began. A transfer backdated past the
    /// rotation's own start would otherwise leave <c>EndDate &lt; StartDate</c> — a negative window
    /// that every date calculation downstream reads as nonsense.
    /// </summary>
    private static void CutShortAt(ServicePeriod period, DateOnly date)
    {
        var cut = date < period.EndDate ? date : period.EndDate;
        period.EndDate = cut > period.StartDate ? cut : period.StartDate;
    }

    /// <summary>
    /// Where the student picks the target slot up: on the transfer date, but never before the slot
    /// opens and never past its close. Transferring in December onto a slot that opens in January
    /// used to produce a period starting in December.
    /// </summary>
    private static DateOnly RemainingWindowStart(DateOnly date, DateOnly slotStart, DateOnly slotEnd)
    {
        var start = date > slotStart ? date : slotStart;
        return start < slotEnd ? start : slotEnd;
    }

    /// <summary>
    /// Plain transfer (no forced remaining-window hand-off). Moves the loaned student's rotation onto
    /// the <b>target</b> group's slot cells so he joins his new colleagues at the same status and the
    /// destination chef gets real, actionable periods instead of a synthesized "incoming" row:
    ///   • the future, not-started rotation is rehomed onto the target group's slots;
    ///   • the rotation <b>currently in progress</b>, if the target group is already running that same
    ///     period, is cut at the transfer date and kept as <see cref="ServicePeriod.IsInterrupted"/>
    ///     history — the origin chef sees "parti vers …" but can no longer evaluate it, and the student
    ///     lands on the target group's started period so the <b>new</b> chef supervises + evaluates.
    /// Completed periods are left untouched (history). No-op — the informational incoming row stays as
    /// the fallback — when the target group has no schedule for the stage yet.
    /// </summary>
    public async Task<Result> MaterializeAtTargetAsync(
        InternshipAssignment assignment, int targetCohortId, DateOnly transferDate, CancellationToken ct)
    {
        var targetSlots = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(sa => sa.CohortId == targetCohortId)
            .Select(sa => new { sa.Id, sa.StageSlotId, sa.ServiceId, sa.StageSlot.StartDate, sa.StageSlot.EndDate })
            .ToListAsync(ct);

        if (targetSlots.Count == 0)
            return Result.Success();

        // The loaned student joins the target group EXACTLY where his new colleagues are for each slot:
        // a slot the group has started gets a started period (visible to the new chef); a slot the group
        // has already CLOSED gets a closed period too, so the student is immediately evaluable alongside
        // them instead of being stranded "en cours" after the administration has clôturé the stage.
        var startedSlotIds = (await dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => p.CohortSlotAssignment!.CohortId == targetCohortId && p.IsStarted && !p.IsInterrupted)
            .Select(p => p.CohortSlotAssignmentId!.Value)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        var openSlotIds = (await dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => p.CohortSlotAssignment!.CohortId == targetCohortId
                     && p.IsStarted && !p.IsComplete && !p.IsInterrupted)
            .Select(p => p.CohortSlotAssignmentId!.Value)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        // A slot the target group has started but where no rotation is still open counts as closed.
        var completeSlotIds = startedSlotIds.Where(id => !openSlotIds.Contains(id)).ToHashSet();

        var targetByStageSlotId = targetSlots.ToDictionary(s => s.StageSlotId);

        // Hand off the in-flight rotation: cut it at the transfer date as terminal history so the old
        // chef can no longer evaluate it, but only when the target group is already running the same
        // period — otherwise the student would land in a not-yet-started void; keep the old period
        // running in that case (the future materialisation below still rehomes everything else).
        foreach (var running in assignment.ServicePeriods
                     .Where(p => p.IsStarted && !p.IsComplete && !p.IsInterrupted
                              && p.CohortSlotAssignment != null)
                     .ToList())
        {
            if (targetByStageSlotId.TryGetValue(running.CohortSlotAssignment!.StageSlotId, out var landing)
                && startedSlotIds.Contains(landing.Id))
            {
                running.IsInterrupted = true;
                CutShortAt(running, transferDate);
            }
        }

        foreach (var stale in assignment.ServicePeriods
                     .Where(p => !p.IsStarted && !p.IsComplete && !p.IsInterrupted)
                     .ToList())
            assignment.ServicePeriods.Remove(stale);

        var coveredSlotIds = assignment.ServicePeriods
            .Where(p => p.CohortSlotAssignmentId is not null)
            .Select(p => p.CohortSlotAssignmentId!.Value)
            .ToHashSet();

        foreach (var slot in targetSlots)
        {
            if (coveredSlotIds.Contains(slot.Id)) continue;

            assignment.ServicePeriods.Add(new ServicePeriod
            {
                InternshipAssignmentId = assignment.Id,
                ServiceId              = slot.ServiceId,
                CohortSlotAssignmentId = slot.Id,
                StartDate              = slot.StartDate,
                EndDate                = slot.EndDate,
                IsStarted              = startedSlotIds.Contains(slot.Id),
                IsComplete             = completeSlotIds.Contains(slot.Id),
            });
        }

        return Result.Success();
    }
}
