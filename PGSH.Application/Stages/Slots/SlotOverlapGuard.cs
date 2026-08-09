using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Slots;

/// <summary>
/// Single source of truth for "the periods of one stage must follow one another without overlapping",
/// within one academic year.
///
/// ⚠ It is deliberately <b>per-stage</b>, not per-level. Until 2026-08-08 this refused any two
/// overlapping windows anywhere in a level, on the reasoning that a level's students follow all of
/// its stages. That reasoning is wrong for the way the faculty actually plans: in
/// <c>example_stage_assignement/Med3.png</c>, Médecine and Chirurgie run the <i>same</i> four windows,
/// with partition A in one and partition B in the other — which is the entire point of splitting a
/// promotion into partitions, since it halves the load on every service. The old rule made that
/// layout unrepresentable, so no level with two concurrent stages could be planned at all.
///
/// Double-booking is still refused, but by the rule that actually expresses it: a slot on its own
/// places nobody, so the check belongs where a group is really put somewhere — see
/// <see cref="GroupScheduleConflictGuard"/>, applied when a <c>CohortSlotAssignment</c> is written.
/// That check is also strictly stronger: it catches a group placed in two overlapping windows, which
/// a windows-only comparison could never tell apart from the legitimate case above.
///
/// The year bounds it. Two promotions never share a student, so 2024-2025's P1 colliding on the
/// calendar with 2025-2026's P1 is not a clash — and without this scope every new year's grid would
/// be refused by the previous one's.
///
/// Two closed windows overlap when <c>a.Start &lt;= b.End &amp;&amp; b.Start &lt;= a.End</c>. Windows are
/// inclusive of both ends, so a period ending 31/03 and the next starting 31/03 <em>do</em> collide —
/// the next one must start 01/04.
/// </summary>
internal sealed class SlotOverlapGuard(IApplicationDbContext dbContext)
{
    /// <summary>
    /// Refuses <paramref name="start"/>–<paramref name="end"/> when it collides with another period
    /// <b>of the same stage</b> in the same year. <paramref name="excludedSlotId"/> lets an update
    /// ignore the slot it is moving; pass null when creating.
    /// </summary>
    public async Task<Result> EnsureNoOverlapAsync(
        int stageId, int academicYearId, int periodNumber, DateOnly start, DateOnly end,
        int? excludedSlotId, CancellationToken ct)
    {
        bool stageExists = await dbContext.Stages.AnyAsync(s => s.Id == stageId, ct);
        if (!stageExists)
            return Result.Failure(StageErrors.NotFound(stageId));

        var conflict = await dbContext.StageSlots
            .AsNoTracking()
            .Where(slot => slot.StageId == stageId)
            .Where(slot => slot.AcademicYearId == academicYearId)
            .Where(slot => excludedSlotId == null || slot.Id != excludedSlotId.Value)
            .Where(slot => slot.StartDate <= end && start <= slot.EndDate)
            .OrderBy(slot => slot.StartDate)
            .Select(slot => new { slot.PeriodNumber, slot.StartDate, slot.EndDate })
            .FirstOrDefaultAsync(ct);

        return conflict is null
            ? Result.Success()
            : Result.Failure(StageErrors.SlotOverlap(
                periodNumber, start, end,
                conflict.PeriodNumber, conflict.StartDate, conflict.EndDate));
    }
}
