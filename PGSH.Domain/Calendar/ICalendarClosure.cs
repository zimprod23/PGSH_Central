namespace PGSH.Domain.Calendar;

/// <summary>
/// Whose calendar a closure belongs to — which is also who it may be hidden from.
/// </summary>
public enum CalendarClosureScope
{
    /// <summary>
    /// The faculty's own calendar: a <see cref="Holiday"/>. Nobody serves, whatever promotion they
    /// are in, so every reader sees it.
    /// </summary>
    Faculty,

    /// <summary>
    /// One promotion's own window: a <see cref="PromotionPause"/>. ⚠ Two promotions rotate through
    /// the same services on the same morning and only one of them is sitting an exam, so a reader
    /// that does not know <em>which</em> promotion it is asking about must not see these at all.
    /// </summary>
    Promotion,
}

/// <summary>
/// A stretch of days on which the people it covers are not in a service, and which therefore does not
/// count toward a stage's duration.
///
/// <para>Two things are that: a <see cref="Holiday"/>, which is the faculty's whole calendar, and a
/// <see cref="PromotionPause"/>, which is one promotion's exam week. They differ in scope and in
/// nothing else, so <see cref="WorkingDayCalendar"/> takes both through this interface rather than
/// growing a second, parallel notion of "days nobody serves".</para>
/// </summary>
public interface ICalendarClosure
{
    /// <summary>Inclusive.</summary>
    DateOnly StartDate { get; }

    /// <summary>Inclusive — the same convention as <c>StageSlot</c>.</summary>
    DateOnly EndDate { get; }

    /// <summary>What to call it on screen: the holiday's name, or the pause's reason.</summary>
    string Name { get; }

    /// <summary>
    /// Whether the dates are settled or still an estimate. Load-bearing in both implementations and
    /// for the same reason: a provisional window still blocks its days — you plan on the best estimate
    /// available — but every window laid over one is flagged, so the répartition can be reprinted when
    /// the dates are settled instead of quietly being a day out.
    /// </summary>
    bool IsConfirmed { get; }

    CalendarClosureScope Scope { get; }
}
