using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Calendar;

namespace PGSH.Application.Calendar;

/// <summary>
/// Builds a <see cref="WorkingDayCalendar"/> from the store — the one place the database side of jours
/// ouvrables is read, so no handler assembles a calendar of its own and gets a different answer.
/// </summary>
/// <remarks>
/// ⚠ <b>There are two calendars, and which one a caller gets is decided by whether it holds a
/// promotion.</b> <see cref="BuildAsync"/> is the faculty's: holidays only, for the Holidays screen and
/// anything genuinely faculty-wide. <see cref="ForPromotionAsync"/> adds that promotion's own
/// <see cref="PromotionPause"/>es, and is what every reader that knows its (année, niveau) must use —
/// two promotions rotate through the same services on the same morning and only one of them is sitting
/// an exam, so a calendar that showed both promotions' windows to both of them would be wrong for each.
/// <b>That split is the whole of the promotion-pause mechanism; the CRUD around it is not.</b>
/// </remarks>
public sealed class WorkingDayProvider(IApplicationDbContext dbContext)
{
    /// <summary>
    /// The faculty's calendar: every holiday and no promotion's pauses.
    /// </summary>
    /// <remarks>
    /// Loads <b>every</b> holiday, deliberately: the table is bounded at roughly fifteen rows a year, and
    /// a date range would have to be widened by an unknown margin anyway — laying ten working days from a
    /// start date can end well past any window the caller could name in advance. Cheaper than getting the
    /// margin wrong.
    /// </remarks>
    public async Task<WorkingDayCalendar> BuildAsync(CancellationToken cancellationToken)
    {
        var holidays = await LoadHolidaysAsync(cancellationToken);

        return WorkingDayCalendar.Build(holidays);
    }

    /// <summary>
    /// One promotion's calendar: the faculty's holidays plus the windows
    /// <paramref name="levelId"/> declared for <paramref name="academicYearId"/>.
    /// </summary>
    /// <param name="levelId">
    /// Null when the caller genuinely spans promotions — a stage export left unscoped by level, say. It
    /// then gets the faculty calendar, because there is no single promotion whose exam weeks would apply
    /// and quietly picking one promotion's would be worse than counting none.
    /// </param>
    /// <param name="excludingPauseId">
    /// The window being previewed or corrected, left out so the impact can be measured. ⚠ Measured
    /// <em>against a calendar that already contains it</em>, a window costs zero worked days — the same
    /// trap <c>HolidayResponse.WorkingDaysLost</c> avoids by counting on the weekend-only calendar.
    /// </param>
    public async Task<WorkingDayCalendar> ForPromotionAsync(
        int academicYearId, int? levelId, CancellationToken cancellationToken, int? excludingPauseId = null)
    {
        var holidays = await LoadHolidaysAsync(cancellationToken);

        if (levelId is not { } level)
            return WorkingDayCalendar.Build(holidays);

        var pauses = await PausesQuery(dbContext, academicYearId, level, excludingPauseId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return WorkingDayCalendar.Build(holidays.Concat<ICalendarClosure>(pauses));
    }

    private Task<List<Holiday>> LoadHolidaysAsync(CancellationToken cancellationToken) =>
        dbContext.Holidays
            .AsNoTracking()
            .OrderBy(h => h.StartDate)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The windows one promotion has declared. Scoped by (année, niveau) and never by either alone: a
    /// level exists in every year and a year holds every promotion, so half the key is not a narrowing,
    /// it is a different question.
    ///
    /// <para>⚠ States no tracking behaviour, and the caller above adds its own. A shared query that
    /// marks itself <c>AsNoTracking</c> makes its <i>host</i> no-tracking too — the defect that had
    /// <c>CnpnTargetPlanner</c>'s apply mutating detached objects and writing nothing.</para>
    /// </summary>
    internal static IQueryable<PromotionPause> PausesQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId, int? excludingPauseId) =>
        dbContext.PromotionPauses
            .Where(p => p.AcademicYearId == academicYearId && p.LevelId == levelId)
            .Where(p => excludingPauseId == null || p.Id != excludingPauseId)
            .OrderBy(p => p.StartDate);
}
