namespace PGSH.Domain.Calendar;

/// <summary>
/// A stretch of days being considered rather than recorded — the window of a preview, before anybody
/// has declared it.
/// </summary>
/// <remarks>
/// Exists so that "what would this window cost" is answered by the same
/// <see cref="WorkingDayCalendar"/> arithmetic that answers "what does this window cost", rather than
/// by a second, parallel calculation that could disagree with it. The preview and the act therefore
/// report the same numbers by construction.
/// </remarks>
/// <param name="WorkedThrough">
/// Whether the proposal is for a closure the faculty works through. Meaningful only on a
/// <see cref="CalendarClosureScope.Faculty"/> proposal — see
/// <see cref="ProposedClosure.CountsAsWorkingDay"/>.
/// </param>
public sealed record ProposedClosure(
    DateOnly StartDate,
    DateOnly EndDate,
    string Name,
    bool IsConfirmed,
    CalendarClosureScope Scope,
    bool WorkedThrough = false) : ICalendarClosure
{
    /// <summary>
    /// Computed rather than taken, so a promotion-scoped proposal cannot claim something
    /// <see cref="PromotionPause"/> is unable to be.
    ///
    /// <para>That aggregate answers <c>false</c> unconditionally — a promotion sitting an exam is not in
    /// a service — and this record exists precisely so a preview and the act it previews cannot report
    /// different numbers. A settable flag here would have been the one field able to make them disagree.</para>
    /// </summary>
    public bool CountsAsWorkingDay => Scope == CalendarClosureScope.Faculty && WorkedThrough;
}
