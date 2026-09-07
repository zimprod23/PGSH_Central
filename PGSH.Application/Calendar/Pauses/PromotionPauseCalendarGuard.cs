using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Calendar;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// The one rule a window cannot check alone, because it is about the <i>other</i> windows: two pauses
/// of one promotion may not overlap.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Load-bearing, not cosmetic — and it is the same rule as
/// <c>AcademicYearCalendarGuard</c>'s, for the same reason.</b> The calendar subtracts a closed day
/// once however many rows cover it, so overlapping windows would not double-subtract from the
/// arithmetic; what they would break is every <i>report</i> of what a window costs. Each one's
/// <c>WorkingDaysLost</c> counts the days in the overlap, so the two together claim more days than the
/// promotion loses, and revoking either one gives back days the other still holds.</para>
///
/// <para>Shared by declare and correct for the reason <c>StudentIdentifierRules</c> exists: a rule
/// enforced on one path only is a row that can be created and then never saved.</para>
/// </remarks>
public sealed class PromotionPauseCalendarGuard(IApplicationDbContext dbContext)
{
    /// <param name="excludingId">The window being corrected, which must not collide with itself.</param>
    public async Task<Result> EnsureFreeAsync(
        int academicYearId,
        int levelId,
        string levelLabel,
        DateOnly startDate,
        DateOnly endDate,
        int? excludingId,
        CancellationToken cancellationToken)
    {
        var others = await WorkingDayProvider
            .PausesQuery(dbContext, academicYearId, levelId, excludingId)
            .AsNoTracking()
            .Select(p => new { p.Reason, p.StartDate, p.EndDate })
            .ToListAsync(cancellationToken);

        // Inclusive on both ends, as the windows themselves are: a pause ending 17/01 and another
        // starting 17/01 share that day.
        var clash = others.FirstOrDefault(p => p.StartDate <= endDate && startDate <= p.EndDate);

        return clash is null
            ? Result.Success()
            : Result.Failure(PromotionPauseErrors.OverlapsAnotherPause(
                levelLabel, clash.Reason, clash.StartDate, clash.EndDate));
    }
}
