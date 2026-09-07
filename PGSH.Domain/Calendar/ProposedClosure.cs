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
public sealed record ProposedClosure(
    DateOnly StartDate,
    DateOnly EndDate,
    string Name,
    bool IsConfirmed,
    CalendarClosureScope Scope) : ICalendarClosure;
