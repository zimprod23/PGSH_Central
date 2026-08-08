using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Slots;

/// <summary>
/// Single source of truth for "no two periods of the same academic level may run at the same time
/// within one academic year".
///
/// A level's students follow every stage of that level, so the constraint is not per-stage: if
/// 3ème année Médecine has Cardiologie and Pédiatrie, a period of one cannot overlap a period of the
/// other — a group would have to be in two services on the same day. Inside one stage the rule is the
/// same thing seen from closer up: consecutive periods must not overlap either.
///
/// The year bounds it. Two different promotions never share a student, so 2024-2025's P1 colliding on
/// the calendar with 2025-2026's P1 is not a clash — and without this scope every new year's grid
/// would be refused by the previous one's.
///
/// Two closed windows overlap when <c>a.Start &lt;= b.End &amp;&amp; b.Start &lt;= a.End</c>. Windows are
/// inclusive of both ends, so a period ending 31/03 and the next starting 31/03 <em>do</em> collide —
/// the next one must start 01/04.
/// </summary>
internal sealed class SlotOverlapGuard(IApplicationDbContext dbContext)
{
    /// <summary>
    /// Refuses <paramref name="start"/>–<paramref name="end"/> when it collides with any other period
    /// of the same level in the same year. <paramref name="excludedSlotId"/> lets an update ignore the
    /// slot it is moving; pass null when creating.
    /// </summary>
    public async Task<Result> EnsureNoOverlapAsync(
        int stageId, int academicYearId, int periodNumber, DateOnly start, DateOnly end,
        int? excludedSlotId, CancellationToken ct)
    {
        int? levelId = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == stageId)
            .Select(s => (int?)s.LevelId)
            .FirstOrDefaultAsync(ct);

        if (levelId is null)
            return Result.Failure(StageErrors.NotFound(stageId));

        var conflict = await dbContext.StageSlots
            .AsNoTracking()
            .Where(slot => slot.Stage.LevelId == levelId.Value)
            .Where(slot => slot.AcademicYearId == academicYearId)
            .Where(slot => excludedSlotId == null || slot.Id != excludedSlotId.Value)
            .Where(slot => slot.StartDate <= end && start <= slot.EndDate)
            // The stage's own periods are reported first: a clash inside the stage being edited is
            // the more actionable message, and its wording differs.
            .OrderByDescending(slot => slot.StageId == stageId)
            .ThenBy(slot => slot.StartDate)
            .Select(slot => new
            {
                slot.PeriodNumber,
                slot.StartDate,
                slot.EndDate,
                StageName = slot.Stage.Name,
                SameStage = slot.StageId == stageId,
            })
            .FirstOrDefaultAsync(ct);

        return conflict is null
            ? Result.Success()
            : Result.Failure(StageErrors.SlotOverlap(
                periodNumber, start, end,
                conflict.StageName, conflict.PeriodNumber, conflict.StartDate, conflict.EndDate,
                conflict.SameStage));
    }
}
