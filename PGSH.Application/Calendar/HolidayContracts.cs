using PGSH.Domain.Calendar;

namespace PGSH.Application.Calendar;

public sealed record HolidayResponse(
    int Id,
    DateOnly StartDate,
    DateOnly EndDate,
    int DayCount,
    string Name,
    HolidayKind Kind,
    bool IsConfirmed,
    // How many worked days it actually costs. A holiday landing on a Sunday costs nothing, and saying so
    // stops it being entered twice or blamed for a window that did not move.
    int WorkingDaysLost);

/// <summary>
/// What a year's calendar is missing, which is the question worth answering — an incomplete calendar does
/// not fail, it silently makes every generated window a few days short.
/// </summary>
/// <param name="MissingReligious">
/// Names from <see cref="MoroccanPublicHolidays.ExpectedReligious"/> with no row in the year's range. Not
/// an error: a year planned in July genuinely does not know next spring's Aïd yet.
/// </param>
public sealed record HolidayCoverageResponse(
    int AcademicYearId,
    string AcademicYearLabel,
    DateOnly From,
    DateOnly To,
    int CalendarDays,
    int WorkingDays,
    int NationalDays,
    int ReligiousDays,
    int AcademicDays,
    int ProvisionalCount,
    IReadOnlyList<string> MissingReligious,
    IReadOnlyList<HolidayResponse> Holidays);
