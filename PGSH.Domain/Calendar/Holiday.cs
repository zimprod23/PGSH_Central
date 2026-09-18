namespace PGSH.Domain.Calendar;

/// <summary>
/// Where a non-working day comes from — which is also how reliable its date is.
/// </summary>
public enum HolidayKind
{
    /// <summary>
    /// A fixed Gregorian date set by law (1ᵉʳ janvier, Fête du Trône…). Known years ahead, so
    /// <see cref="MoroccanPublicHolidays.FixedFor"/> can generate them rather than have them typed.
    /// </summary>
    National,

    /// <summary>
    /// A lunar date (Aïd al-Fitr, Aïd al-Adha, Mawlid, 1ᵉʳ Moharram). ⚠ These <b>cannot be computed</b>:
    /// in Morocco the month turns on observation of the crescent and the days off are announced by
    /// decree, so an estimate can move by a day in either direction. They are entered, and carry
    /// <see cref="Holiday.IsConfirmed"/>.
    /// </summary>
    Religious,

    /// <summary>
    /// A closure the faculty or hospital declares itself — vacances, journée pédagogique, grève. Not a
    /// public holiday, but just as much a day no student is in a service.
    /// </summary>
    Academic,
}

/// <summary>
/// A stretch of days on which no student is in a service, and which therefore does not count toward a
/// stage's duration.
///
/// <para>Spans rather than single dates because the holidays that matter most here are multi-day: Aïd
/// al-Adha is two days, and vacances universitaires are two weeks. One row per day would make the
/// common case the tedious one.</para>
/// </summary>
public sealed class Holiday : ICalendarClosure
{
    public int Id { get; set; }

    /// <summary>Inclusive. A one-day holiday has <see cref="EndDate"/> equal to this.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Inclusive — the same convention as <c>StageSlot</c>.</summary>
    public DateOnly EndDate { get; set; }

    public string Name { get; set; } = string.Empty;

    public HolidayKind Kind { get; set; } = HolidayKind.National;

    /// <summary>
    /// Whether the date is settled or still an estimate. Load-bearing, not bookkeeping: a provisional
    /// Aïd still blocks the days (you plan on the best estimate available), but every window generated
    /// over one is flagged, so a répartition can be reprinted when the decree lands instead of quietly
    /// being a day out. Fixed national dates are confirmed by construction.
    /// </summary>
    public bool IsConfirmed { get; set; } = true;

    /// <summary>
    /// Whether the faculty works through this closure. <c>false</c> by default and on every row in the
    /// base today, which is the old arithmetic unchanged: the days neither count toward a stage's
    /// duration nor may bound a window.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Setting it true changes what every window laid over the date is worth</b>, exactly as moving
    /// the date does — which is why <c>UpdateHolidayResult</c> reports the créneaux spanning it on this
    /// change too, and not only when the dates move. ⚠ And it never makes the day <i>bounding</i>: a
    /// stage may run through the Fête du Trône and still must not end on it.
    /// </remarks>
    public bool CountsAsWorkingDay { get; set; }

    /// <summary>
    /// The faculty's whole calendar — a holiday is nobody's exam week. See
    /// <see cref="PromotionPause"/> for the closure that belongs to one promotion.
    /// </summary>
    public CalendarClosureScope Scope => CalendarClosureScope.Faculty;

    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;

    public bool Covers(DateOnly date) => date >= StartDate && date <= EndDate;
}
