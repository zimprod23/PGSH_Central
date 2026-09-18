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
/// Inclusive, and always a day that can <em>bound</em> a window — never a rest day and never a closure,
/// not even one the faculty works through. Trailing weekends are not swallowed into the window, so two
/// consecutive windows do not overlap a rest day between them.
/// </param>
/// <param name="WorkingDays">
/// What the window <b>holds</b>, counted with <see cref="WorkingDayCalendar.CountsTowardDuration"/> —
/// never what was asked for. The two differ only in the case described on
/// <see cref="RunsLongerThanAsked"/>; comparing them is how a caller sees it.
/// </param>
/// <param name="RequestedWorkingDays">What the caller asked <see cref="WorkingDayCalendar.Lay"/> for.</param>
/// <param name="CalendarDays">How long the window is on a wall calendar, for comparison with the count.</param>
/// <param name="HolidaysHit">
/// The closures that fell inside, in date order — what makes the gap explainable. Faculty holidays and,
/// when the calendar was built for a promotion, that promotion's own pauses.
/// </param>
public sealed record WorkingDayWindow(
    DateOnly Start,
    DateOnly End,
    int WorkingDays,
    int RequestedWorkingDays,
    int CalendarDays,
    IReadOnlyList<ICalendarClosure> HolidaysHit)
{
    /// <summary>True when a date inside the window is still an estimate, so the window may move.</summary>
    public bool HasProvisionalDates => HolidaysHit.Any(c => !c.IsConfirmed);

    /// <summary>
    /// True when the window had to take on days nobody asked for, because the day the count ran out on
    /// could not bound it.
    ///
    /// <para>It happens for one reason: the Nᵗʰ counted day fell on a closure the faculty works
    /// <em>through</em> (<see cref="ICalendarClosure.CountsAsWorkingDay"/>). Such a day counts but may
    /// not end a window, so <see cref="End"/> advances to the next day that can — and any further worked
    /// closure days crossed on the way are <b>counted</b>, because people serve them. The window is
    /// therefore honest at the price of being a day or two long, and this says so instead of leaving a
    /// caller to notice that one column of an axis is wider than its neighbours.</para>
    /// </summary>
    /// <remarks>
    /// ⚠ <c>false</c> for every window the base can produce today: no closure is flagged. It becomes
    /// reachable the first time a férié is marked worked.
    /// </remarks>
    public bool RunsLongerThanAsked => WorkingDays > RequestedWorkingDays;
}

/// <summary>
/// Counts and lays out <em>jours ouvrables</em>: calendar days minus the weekly rest days and minus the
/// declared <see cref="ICalendarClosure"/>s — the faculty's holidays, plus one promotion's own pauses when
/// the calendar was built for a promotion.
///
/// <para>⚠ <b>It answers two questions, not one, and they are separate methods on purpose.</b>
/// <see cref="CountsTowardDuration"/> — is somebody expected in a service — and
/// <see cref="CanBoundAWindow"/> — may a période begin or end here. A single <c>IsWorkingDay</c> conflated
/// them, which made a closure impossible to <i>cross</i>: marking a férié worked would also have let a
/// stage end on it. The predicates are nested (every bounding day counts, not every counted day bounds),
/// never independent.</para>
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

    private readonly List<ICalendarClosure> _closures;

    /// <summary>Every day any closure covers — none of them may bound a window.</summary>
    private readonly HashSet<DateOnly> _closed;

    /// <summary>
    /// The subset that also does not count toward a duration: closures the faculty does <em>not</em>
    /// work through. A closure flagged <see cref="ICalendarClosure.CountsAsWorkingDay"/> is in
    /// <see cref="_closed"/> and not in here, which is the whole of the distinction.
    /// </summary>
    private readonly HashSet<DateOnly> _uncounted;

    public WorkingWeek Week { get; }

    private WorkingDayCalendar(WorkingWeek week, List<ICalendarClosure> closures)
    {
        Week = week;
        _closures = closures;
        _closed = closures.SelectMany(DaysOf).ToHashSet();
        _uncounted = closures.Where(c => !c.CountsAsWorkingDay).SelectMany(DaysOf).ToHashSet();
    }

    private static IEnumerable<DateOnly> DaysOf(ICalendarClosure closure) =>
        Enumerable
            .Range(0, Math.Max(1, closure.EndDate.DayNumber - closure.StartDate.DayNumber + 1))
            .Select(offset => closure.StartDate.AddDays(offset));

    /// <summary>
    /// A calendar with no holidays — weekends only. Useful as a floor: it is what the arithmetic would
    /// give if nobody had entered a single holiday, which is the state the base starts in.
    /// </summary>
    public static WorkingDayCalendar WeekendsOnly(WorkingWeek? week = null) =>
        new(week ?? WorkingWeek.Moroccan, []);

    public static WorkingDayCalendar Build(IEnumerable<ICalendarClosure> closures, WorkingWeek? week = null) =>
        new(week ?? WorkingWeek.Moroccan, closures.OrderBy(c => c.StartDate).ToList());

    /// <summary>
    /// The same calendar with one more closure — what it <em>would</em> be if <paramref name="closure"/>
    /// were declared.
    /// </summary>
    /// <remarks>
    /// This is how a window's cost is measured: the days it takes are the difference between a count on
    /// this calendar and a count on that one. ⚠ Asked of a calendar that <i>already</i> contains the
    /// window, the answer is always zero — the same trap <c>HolidayResponse.WorkingDaysLost</c> avoids by
    /// counting against the weekend-only calendar.
    /// </remarks>
    public WorkingDayCalendar With(ICalendarClosure closure) =>
        Build(_closures.Append(closure), Week);

    /// <summary>
    /// Whether this day counts toward a stage's duration — whether somebody is expected in a service.
    ///
    /// <para>One of the <b>two</b> questions a calendar is asked, and the reason there is no single
    /// <c>IsWorkingDay</c> any more: it answered both at once, so a closure could not be crossed without
    /// also lengthening every window that crossed it. A rest day counts for nobody; a closure counts
    /// only when it is flagged <see cref="ICalendarClosure.CountsAsWorkingDay"/>.</para>
    /// </summary>
    public bool CountsTowardDuration(DateOnly date) =>
        !Week.IsRestDay(date) && !_uncounted.Contains(date);

    /// <summary>
    /// Whether a window may <b>begin</b> or <b>end</b> on this day. The faculty's only planning
    /// constraint (10/09/2026), and the other of the two questions.
    ///
    /// <para>⚠ Strictly narrower than <see cref="CountsTowardDuration"/>, and deliberately blind to
    /// <see cref="ICalendarClosure.CountsAsWorkingDay"/>: a stage may run <i>through</i> the Fête du
    /// Trône and still must not start or finish on it. Every bounding day counts; not every counted day
    /// bounds.</para>
    /// </summary>
    public bool CanBoundAWindow(DateOnly date) => !Week.IsRestDay(date) && !_closed.Contains(date);

    /// <summary>
    /// The first day at or after <paramref name="from"/> that can bound a window, or null past the scan
    /// horizon.
    /// </summary>
    public DateOnly? NextBoundingDay(DateOnly from)
    {
        for (int i = 0; i < MaxScanDays; i++)
        {
            var day = from.AddDays(i);
            if (CanBoundAWindow(day)) return day;
        }

        return null;
    }

    /// <summary>Worked days in <paramref name="from"/>…<paramref name="toInclusive"/>. Zero when reversed.</summary>
    public int Count(DateOnly from, DateOnly toInclusive)
    {
        if (toInclusive < from) return 0;

        int count = 0;
        for (var day = from; day <= toInclusive; day = day.AddDays(1))
            if (CountsTowardDuration(day)) count++;

        return count;
    }

    public IReadOnlyList<ICalendarClosure> HolidaysBetween(DateOnly from, DateOnly toInclusive) =>
        _closures.Where(c => c.EndDate >= from && c.StartDate <= toInclusive).ToList();

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
        // ⚠ Faculty closures only. A promotion pause named « Aïd » is a promotion saying it is out that
        // week, not the decree naming the date — counting it would report the faculty's calendar complete
        // on the strength of one promotion's own window.
        var recorded = HolidaysBetween(new DateOnly(from.Year, 1, 1), new DateOnly(toInclusive.Year, 12, 31))
            .Where(c => c.Scope == CalendarClosureScope.Faculty)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return MoroccanPublicHolidays.ExpectedReligious
            .Where(e => !recorded.Contains(e.Name))
            .Select(e => e.Name)
            .ToList();
    }

    /// <summary>
    /// Lays <paramref name="workingDays"/> worked days out from <paramref name="start"/>, stepping over
    /// rest days and over every closure that does not count. Returns null when
    /// <paramref name="workingDays"/> is not positive or the horizon is exhausted.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>The window it returns may hold more days than were asked for, and it says so.</b> The
    /// promise that cannot be broken is that <see cref="WorkingDayWindow.End"/> is a day a window may end
    /// on. When the count runs out on a closure the faculty works <em>through</em> — counted, but never
    /// bounding — the end advances to the next day that can bound, and the worked days crossed on the way
    /// are counted rather than quietly dropped.</para>
    ///
    /// <para><b>Why counted and not dropped.</b> Not counting them would leave
    /// <c>Count(Start, End)</c> disagreeing with <see cref="WorkingDayWindow.WorkingDays"/> for the same
    /// window — one number for two facts, in the class every other number in this codebase is measured
    /// against. The divergence is reported instead, through
    /// <see cref="WorkingDayWindow.RunsLongerThanAsked"/>.</para>
    ///
    /// <para>⚠ Unreachable on the base as it stands: no closure is flagged, so every window holds exactly
    /// what it was asked for and this is the old behaviour line for line.</para>
    /// </remarks>
    public WorkingDayWindow? Lay(DateOnly start, int workingDays)
    {
        if (workingDays < 1 || !Week.IsUsable) return null;

        var first = NextBoundingDay(start);
        if (first is null) return null;

        var cursor = first.Value;

        // The first day bounds, and every bounding day counts — the two predicates are nested, never
        // independent — so the window opens on one worked day.
        int found = 1;

        // Two conditions rather than one: the count has to be reached, *and* the day it is reached on has
        // to be one a window may end on. They coincide everywhere except across a worked closure, which
        // is the whole reason this loop is not a simple countdown.
        while (found < workingDays || !CanBoundAWindow(cursor))
        {
            cursor = cursor.AddDays(1);
            if (cursor.DayNumber - first.Value.DayNumber > MaxScanDays) return null;
            if (CountsTowardDuration(cursor)) found++;
        }

        return new WorkingDayWindow(
            first.Value,
            cursor,
            found,
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
