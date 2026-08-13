namespace PGSH.Domain.Calendar;

/// <summary>
/// Which days of the week are not worked. Morocco's public sector rests Saturday and Sunday, and that is
/// the default everywhere in PGSH.
/// </summary>
/// <remarks>
/// ⚠ A hospital service is not a public office: many run Saturday mornings, and a garde runs every day of
/// the year. A per-service working week is deliberately <b>not</b> modelled — the calendar here answers
/// "how long is this stage in calendar days", which is a planning question about the promotion, not an
/// attendance question about one student. Attendance is recorded per day against
/// <c>AttendanceRecord</c> and is not derived from this.
/// </remarks>
public sealed record WorkingWeek(IReadOnlySet<DayOfWeek> RestDays)
{
    public static readonly WorkingWeek Moroccan =
        new(new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday });

    /// <summary>For a service that closes only on Sunday.</summary>
    public static readonly WorkingWeek SundayOnly =
        new(new HashSet<DayOfWeek> { DayOfWeek.Sunday });

    public bool IsRestDay(DateOnly date) => RestDays.Contains(date.DayOfWeek);

    public bool IsUsable => RestDays.Count < 7;
}

/// <summary>
/// The result of laying <paramref name="WorkingDays"/> worked days out on the calendar from a start date.
/// </summary>
/// <param name="Start">
/// The first <em>worked</em> day at or after the requested start. A block asked to begin on a Saturday
/// begins on the Monday: a window whose first day nobody attends misreports its own length.
/// </param>
/// <param name="End">
/// Inclusive, and always a worked day — the day the last one falls on. Trailing weekends are not
/// swallowed into the window, so two consecutive windows do not overlap a rest day between them.
/// </param>
/// <param name="CalendarDays">How long the window is on a wall calendar, for comparison with the count.</param>
/// <param name="HolidaysHit">The holidays that fell inside, in date order — what makes the gap explainable.</param>
public sealed record WorkingDayWindow(
    DateOnly Start,
    DateOnly End,
    int WorkingDays,
    int CalendarDays,
    IReadOnlyList<Holiday> HolidaysHit)
{
    /// <summary>True when a date inside the window is still an estimate, so the window may move.</summary>
    public bool HasProvisionalDates => HolidaysHit.Any(h => !h.IsConfirmed);
}

/// <summary>
/// Counts and lays out <em>jours ouvrables</em>: calendar days minus the weekly rest days and minus every
/// declared <see cref="Holiday"/>.
///
/// <para>Pure and immutable — built once from the holiday table, then asked as many questions as needed.
/// It holds no clock and no database, which is what lets the awkward cases (a window starting on a
/// holiday, a stage spanning Aïd) be tested exhaustively rather than argued about.</para>
/// </summary>
/// <remarks>
/// ⚠ <b>This never converts <c>Stage.DurationInDays</c>.</b> Measured 2026-08-13, that column is already
/// in worked days for 25 of 27 stages (14×7, 22×7, 30×2, 42×3, 44×6, 66×2 — 22 being a month of worked
/// days). The two 30s are the ambiguous ones. Either way the calendar is used only where a duration is
/// <em>stated in working days at the point of use</em> — generating an axis — and everywhere else it
/// reports rather than converts, because which column is authoritative is still open. See
/// <c>PHASES.md</c> 15.1.
/// </remarks>
public sealed class WorkingDayCalendar
{
    /// <summary>
    /// Bounds the forward scan so a pathological calendar (a year declared entirely non-working) cannot
    /// spin. Ten years is far past any stage.
    /// </summary>
    private const int MaxScanDays = 3_650;

    private readonly List<Holiday> _holidays;
    private readonly HashSet<DateOnly> _closed;

    public WorkingWeek Week { get; }

    private WorkingDayCalendar(WorkingWeek week, List<Holiday> holidays)
    {
        Week = week;
        _holidays = holidays;
        _closed = holidays
            .SelectMany(h => Enumerable
                .Range(0, Math.Max(1, h.DayCount))
                .Select(offset => h.StartDate.AddDays(offset)))
            .ToHashSet();
    }

    /// <summary>
    /// A calendar with no holidays — weekends only. Useful as a floor: it is what the arithmetic would
    /// give if nobody had entered a single holiday, which is the state the base starts in.
    /// </summary>
    public static WorkingDayCalendar WeekendsOnly(WorkingWeek? week = null) =>
        new(week ?? WorkingWeek.Moroccan, []);

    public static WorkingDayCalendar Build(IEnumerable<Holiday> holidays, WorkingWeek? week = null) =>
        new(week ?? WorkingWeek.Moroccan, holidays.OrderBy(h => h.StartDate).ToList());

    public bool IsWorkingDay(DateOnly date) => !Week.IsRestDay(date) && !_closed.Contains(date);

    /// <summary>The first worked day at or after <paramref name="from"/>, or null past the scan horizon.</summary>
    public DateOnly? NextWorkingDay(DateOnly from)
    {
        for (int i = 0; i < MaxScanDays; i++)
        {
            var day = from.AddDays(i);
            if (IsWorkingDay(day)) return day;
        }

        return null;
    }

    /// <summary>Worked days in <paramref name="from"/>…<paramref name="toInclusive"/>. Zero when reversed.</summary>
    public int Count(DateOnly from, DateOnly toInclusive)
    {
        if (toInclusive < from) return 0;

        int count = 0;
        for (var day = from; day <= toInclusive; day = day.AddDays(1))
            if (IsWorkingDay(day)) count++;

        return count;
    }

    public IReadOnlyList<Holiday> HolidaysBetween(DateOnly from, DateOnly toInclusive) =>
        _holidays.Where(h => h.EndDate >= from && h.StartDate <= toInclusive).ToList();

    /// <summary>
    /// Which of the lunar holidays a complete calendar needs have no row recorded near
    /// <paramref name="from"/>…<paramref name="toInclusive"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ **Widened to whole Gregorian years on purpose.** A Hijri date drifts about eleven days earlier
    /// each year, so a lunar holiday lands anywhere in the Gregorian calendar, and asking "is Aïd
    /// recorded?" of a narrow span answers a different question than intended: a four-column axis over
    /// October–January would report *every* spring holiday as missing, and a 1 September – 31 July
    /// academic year would report an August Mawlid missing forever even though it is on file. The only
    /// span in which the answer is stable is the whole year.
    /// </remarks>
    public IReadOnlyList<string> MissingReligious(DateOnly from, DateOnly toInclusive)
    {
        var recorded = HolidaysBetween(new DateOnly(from.Year, 1, 1), new DateOnly(toInclusive.Year, 12, 31))
            .Select(h => h.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return MoroccanPublicHolidays.ExpectedReligious
            .Where(e => !recorded.Contains(e.Name))
            .Select(e => e.Name)
            .ToList();
    }

    /// <summary>
    /// Lays <paramref name="workingDays"/> worked days out from <paramref name="start"/>, skipping rest
    /// days and holidays. Returns null when <paramref name="workingDays"/> is not positive or the horizon
    /// is exhausted.
    /// </summary>
    public WorkingDayWindow? Lay(DateOnly start, int workingDays)
    {
        if (workingDays < 1 || !Week.IsUsable) return null;

        var first = NextWorkingDay(start);
        if (first is null) return null;

        var cursor = first.Value;
        int found = 1;

        while (found < workingDays)
        {
            cursor = cursor.AddDays(1);
            if (cursor.DayNumber - first.Value.DayNumber > MaxScanDays) return null;
            if (IsWorkingDay(cursor)) found++;
        }

        return new WorkingDayWindow(
            first.Value,
            cursor,
            workingDays,
            cursor.DayNumber - first.Value.DayNumber + 1,
            HolidaysBetween(first.Value, cursor));
    }

    /// <summary>
    /// Lays <paramref name="count"/> consecutive windows of <paramref name="workingDaysEach"/> worked days
    /// each, the next beginning on the first worked day after the previous one ends.
    ///
    /// <para>This is what an axis is: one start date and one length, expanded into the T columns every
    /// stage of the block then shares. Returns fewer than <paramref name="count"/> windows only if the
    /// horizon is exhausted, which the caller must treat as a failure rather than a short axis.</para>
    /// </summary>
    public IReadOnlyList<WorkingDayWindow> LaySeries(DateOnly start, int count, int workingDaysEach)
    {
        var windows = new List<WorkingDayWindow>(Math.Max(0, count));
        var cursor = start;

        for (int i = 0; i < count; i++)
        {
            var window = Lay(cursor, workingDaysEach);
            if (window is null) break;

            windows.Add(window);
            cursor = window.End.AddDays(1);
        }

        return windows;
    }
}
