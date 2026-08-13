using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Calendar;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar;

/// <summary>
/// The academic year's calendar: its holidays, how many worked days it leaves, and what is still missing.
/// </summary>
/// <remarks>
/// Scoped to the year's own <c>StartDate</c>…<c>EndDate</c> rather than a Gregorian year, because that is
/// the span an axis is laid across. Holidays themselves are not year-stamped — a date belongs to the
/// calendar, not to a promotion — so this is a read that filters by range, not a year-constituted table.
/// </remarks>
public sealed record GetHolidayCoverageQuery(int? AcademicYearId = null)
    : IQuery<HolidayCoverageResponse>;

internal sealed class GetHolidayCoverageQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : IQueryHandler<GetHolidayCoverageQuery, HolidayCoverageResponse>
{
    public async Task<Result<HolidayCoverageResponse>> Handle(
        GetHolidayCoverageQuery request, CancellationToken cancellationToken)
    {
        var resolved = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<HolidayCoverageResponse>(resolved.Error);

        var year = await dbContext.AcademicYears
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == resolved.Value, cancellationToken);

        if (year is null)
            return Result.Failure<HolidayCoverageResponse>(
                Error.NotFound("AcademicYears.NotFound", $"Année {resolved.Value} introuvable."));

        var holidays = await dbContext.Holidays
            .AsNoTracking()
            .Where(h => h.EndDate >= year.StartDate && h.StartDate <= year.EndDate)
            .OrderBy(h => h.StartDate)
            .ToListAsync(cancellationToken);

        // The full calendar, not just this year's rows: a stage beginning in late June is laid across
        // dates the year does not contain, and the count below must agree with what the axis generator
        // will do.
        var calendar = await new WorkingDayProvider(dbContext).BuildAsync(cancellationToken);

        var recorded = holidays.Select(h => h.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingReligious = MoroccanPublicHolidays.ExpectedReligious
            .Where(e => !recorded.Contains(e.Name))
            .Select(e => e.Name)
            .ToList();

        return new HolidayCoverageResponse(
            year.Id,
            year.Label,
            year.StartDate,
            year.EndDate,
            year.EndDate.DayNumber - year.StartDate.DayNumber + 1,
            calendar.Count(year.StartDate, year.EndDate),
            holidays.Count(h => h.Kind == HolidayKind.National),
            holidays.Count(h => h.Kind == HolidayKind.Religious),
            holidays.Count(h => h.Kind == HolidayKind.Academic),
            holidays.Count(h => !h.IsConfirmed),
            missingReligious,
            holidays.Select(h => Map(h, calendar)).ToList());
    }

    /// <summary>
    /// <c>WorkingDaysLost</c> is counted against the weekend-only calendar, not the full one: measured
    /// against a calendar that already contains this holiday, every holiday costs zero.
    /// </summary>
    private static HolidayResponse Map(Holiday holiday, WorkingDayCalendar calendar)
    {
        var weekendsOnly = WorkingDayCalendar.WeekendsOnly(calendar.Week);

        return new HolidayResponse(
            holiday.Id,
            holiday.StartDate,
            holiday.EndDate,
            holiday.DayCount,
            holiday.Name,
            holiday.Kind,
            holiday.IsConfirmed,
            weekendsOnly.Count(holiday.StartDate, holiday.EndDate));
    }
}
