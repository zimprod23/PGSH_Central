using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Calendar;

namespace PGSH.Application.Calendar;

/// <summary>
/// Builds a <see cref="WorkingDayCalendar"/> from the holiday table — the one place the database side of
/// jours ouvrables is read, so no handler assembles a calendar of its own and gets a different answer.
/// </summary>
public sealed class WorkingDayProvider(IApplicationDbContext dbContext)
{
    /// <summary>
    /// Loads <b>every</b> holiday, deliberately: the table is bounded at roughly fifteen rows a year, and
    /// a date range would have to be widened by an unknown margin anyway — laying ten working days from a
    /// start date can end well past any window the caller could name in advance. Cheaper than getting the
    /// margin wrong.
    /// </summary>
    public async Task<WorkingDayCalendar> BuildAsync(CancellationToken cancellationToken)
    {
        var holidays = await dbContext.Holidays
            .AsNoTracking()
            .OrderBy(h => h.StartDate)
            .ToListAsync(cancellationToken);

        return WorkingDayCalendar.Build(holidays);
    }
}
