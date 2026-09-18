using PGSH.Domain.Calendar;

namespace PGSH.Application.Calendar;

/// <param name="CountsAsWorkingDay">
/// Whether the faculty works through it. ⚠ <b>Carried here because this response feeds the edit form</b>
/// — a summary that omits a field the form writes back is how editing a hospital came to erase its
/// description.
/// </param>
/// <param name="WorkingDaysLost">
/// How many worked days it actually costs. A holiday landing on a Sunday costs nothing, and one flagged
/// <paramref name="CountsAsWorkingDay"/> costs nothing either — saying so stops it being entered twice or
/// blamed for a window that did not move.
///
/// <para>⚠ <b>Zero has two opposite meanings here and the screen must not print it alone</b>: « férié,
/// chômé, mais tombé un dimanche » is a row to leave alone, « férié, travaillé » is a row somebody
/// deliberately flagged and may want to unflag. The two are separated by
/// <paramref name="CountsAsWorkingDay"/>, which is why it travels beside this number rather than being
/// inferred from it.</para>
/// </param>
public sealed record HolidayResponse(
    int Id,
    DateOnly StartDate,
    DateOnly EndDate,
    int DayCount,
    string Name,
    HolidayKind Kind,
    bool IsConfirmed,
    bool CountsAsWorkingDay,
    int WorkingDaysLost);

/// <summary>
/// What a year's calendar is missing, which is the question worth answering — an incomplete calendar does
/// not fail, it silently makes every generated window a few days short.
/// </summary>
/// <param name="MissingReligious">
/// Names from <see cref="MoroccanPublicHolidays.ExpectedReligious"/> with no row in the year's range. Not
/// an error: a year planned in July genuinely does not know next spring's Aïd yet.
/// </param>
/// <param name="WorkedThroughCount">
/// How many of the year's holidays the faculty works through. ⚠ Reported so that
/// <paramref name="WorkingDays"/> — which counts them — cannot be read as a miscount: without this
/// number a reader comparing the total against the list has no way to tell an arithmetic bug from a
/// deliberate flag.
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
    int WorkedThroughCount,
    IReadOnlyList<string> MissingReligious,
    IReadOnlyList<HolidayResponse> Holidays);
