using PGSH.Domain.Calendar;

namespace PGSH.Application.Stages.Export;

/// <summary>
/// Turns the périodes of one stage attempt into the single line a document prints for it.
///
/// <para><b>The question this class answers.</b> A stage occupying several columns of the axis can be
/// one stay or several: 01/01→01/02 then 02/02→02/03 is <em>one</em> rotation written twice when the
/// service never changed and the two windows meet, and <em>two</em> when they do not. Printed as the
/// bare span « 01/01/2025 – 02/03/2025 » the first is right and the second asserts the student stood
/// in a service on days he was somewhere else; printed as two windows always, the ordinary
/// single-service stage becomes unreadable for no gain.</para>
///
/// <para><b>The rule: merge on the service, never on the dates.</b> A <em>stay</em> is a maximal run
/// of consecutive périodes in the <b>same service</b> with <b>no worked day between them</b>. One
/// stay prints as one span. Several stays print as several, joined — and the services print in the
/// same order, so column and column correspond position by position.</para>
///
/// <para>⚠ <b>The multi-period fact is never carried by the string alone.</b>
/// <see cref="StagePeriodSummary.PeriodCount"/> and <see cref="StagePeriodSummary.ServiceCount"/>
/// are numbers in their own columns, so « montre-moi les stages faits en deux services » is a filter
/// rather than a reading exercise — and <see cref="StagePeriodSummary.Shape"/> says in words which
/// of the four cases the row is.</para>
///
/// <para><b>Most stages arrive here already collapsed.</b> <c>SchedulePublisher</c> folds a
/// <c>SingleService</c> run into one <c>ServicePeriod</c> spanning it, and 5ᵉ/6ᵉ année are
/// <c>SingleService</c> in 51 923 of 51 924 imported placements — so the common row is one période
/// and the folding is invisible. It exists for 3ᵉ and 4ᵉ année, which genuinely rotate, and for the
/// history the Access import carried in one row per stay.</para>
///
/// <para>Pure — no database, no clock — which is what lets the cases be tested exhaustively, the same
/// reason <c>PeriodAxis</c>, <c>RotationTiling</c> and <c>OccupancyTimeline</c> are.</para>
/// </summary>
public static class StagePeriodFolder
{
    /// <summary>Between two spans on the same row; reads as « puis ».</summary>
    private const string SpanSeparator = " · ";

    /// <summary>Between two services: an itinerary, not a set.</summary>
    private const string ServiceSeparator = " → ";

    public static StagePeriodSummary Fold(
        IReadOnlyList<ExportedPeriod> periods, WorkingDayCalendar calendar)
    {
        if (periods.Count == 0)
            return StagePeriodSummary.Empty;

        var ordered = periods
            .OrderBy(p => p.Start)
            .ThenBy(p => p.End)
            .ToList();

        var stays = BuildStays(ordered, calendar);

        int serviceCount = ordered.Select(p => p.ServiceId).Distinct().Count();

        // ⚠ Summed over the périodes, not measured end-to-end. An interrupted stage's span contains
        // days nobody served, and a duration read off `Fin - Début` is exactly the number that makes
        // a 22-jour stage look like a 60-jour one.
        int workingDays = ordered.Sum(p => calendar.Count(p.Start, p.End));
        int calendarDays = ordered.Sum(p => p.End.DayNumber - p.Start.DayNumber + 1);

        return new StagePeriodSummary(
            PeriodCount: ordered.Count,
            ServiceCount: serviceCount,
            Stays: stays,
            Start: ordered[0].Start,
            End: ordered.Max(p => p.End),
            WorkingDays: workingDays,
            CalendarDays: calendarDays,
            Shape: ShapeOf(ordered.Count, serviceCount, stays.Count),
            Periods: ordered);
    }

    /// <summary>
    /// ⚠ Two conditions, and dropping either one is a different defect. Breaking only on the service
    /// change (the shape <c>SchedulePublisher.BuildStays</c> uses, correctly, because it works from
    /// contiguous grid columns) would swallow a real interruption inside one printed span. Breaking
    /// only on the gap would merge a genuine S1 → S2 rotation into one line and lose the second
    /// service entirely.
    /// </summary>
    private static List<ExportedStay> BuildStays(
        IReadOnlyList<ExportedPeriod> ordered, WorkingDayCalendar calendar)
    {
        var stays = new List<ExportedStay>();

        var openServiceId = ordered[0].ServiceId;
        string openServiceName = ordered[0].ServiceName;
        var openStart = ordered[0].Start;
        var openEnd = ordered[0].End;
        int openPeriods = 1;

        for (int i = 1; i < ordered.Count; i++)
        {
            var period = ordered[i];
            bool continues = period.ServiceId == openServiceId && MeetsWithoutGap(openEnd, period.Start, calendar);

            if (continues)
            {
                openEnd = period.End > openEnd ? period.End : openEnd;
                openPeriods++;
                continue;
            }

            stays.Add(new ExportedStay(openServiceId, openServiceName, openStart, openEnd, openPeriods));
            (openServiceId, openServiceName, openStart, openEnd, openPeriods) =
                (period.ServiceId, period.ServiceName, period.Start, period.End, 1);
        }

        stays.Add(new ExportedStay(openServiceId, openServiceName, openStart, openEnd, openPeriods));

        return stays;
    }

    /// <summary>
    /// No <b>worked</b> day strictly between the two windows. A calendar-day test would call every
    /// Friday→Monday hand-over an interruption — which is the ordinary way one column follows
    /// another, since <c>WorkingDayCalendar</c> never lets a window swallow its trailing weekend.
    /// </summary>
    private static bool MeetsWithoutGap(DateOnly openEnd, DateOnly nextStart, WorkingDayCalendar calendar) =>
        calendar.Count(openEnd.AddDays(1), nextStart.AddDays(-1)) == 0;

    private static StagePeriodShape ShapeOf(int periodCount, int serviceCount, int stayCount) =>
        (periodCount, serviceCount, stayCount) switch
        {
            (1, _, _)         => StagePeriodShape.Single,
            (_, 1, 1)         => StagePeriodShape.SingleServiceContiguous,
            (_, 1, _)         => StagePeriodShape.SingleServiceInterrupted,
            _                 => StagePeriodShape.MultiService,
        };

    internal static string ServicesText(StagePeriodSummary summary) => summary.ServiceCount switch
    {
        0 => "",
        // One service written once, however many windows it was recorded in — repeating the same
        // name either side of an arrow reads as a rotation that never happened.
        1 => summary.Stays[0].ServiceName,
        _ => string.Join(ServiceSeparator, summary.Stays.Select(s => s.ServiceName)),
    };

    internal static string PeriodsText(StagePeriodSummary summary) =>
        string.Join(SpanSeparator, summary.Stays.Select(s => Span(s.Start, s.End)));

    internal static string ShapeText(StagePeriodSummary summary) => summary.Shape switch
    {
        StagePeriodShape.Single => "Période unique",
        StagePeriodShape.SingleServiceContiguous =>
            $"Service unique — {summary.PeriodCount} périodes contiguës",
        StagePeriodShape.SingleServiceInterrupted =>
            $"Service unique — {summary.PeriodCount} périodes, {summary.Stays.Count - 1} interruption(s)",
        StagePeriodShape.MultiService =>
            $"Rotation — {summary.ServiceCount} services, {summary.PeriodCount} périodes",
        _ => "Aucune période",
    };

    public static string Span(DateOnly start, DateOnly end) => $"{Day(start)} – {Day(end)}";

    public static string Day(DateOnly date) => date.ToString("dd/MM/yyyy");
}

/// <summary>
/// One période, reduced to what the folding needs. Deliberately not <c>ServicePeriod</c>: the rule is
/// about dates and services and nothing else, and a pure input is what makes it testable without a
/// store.
/// </summary>
public sealed record ExportedPeriod(
    Guid Id,
    DateOnly Start,
    DateOnly End,
    int ServiceId,
    string ServiceName);

/// <summary>
/// A run the student actually stood through, in one service, without a hole.
/// <see cref="ServiceId"/> is what lets a document name the stay's <em>chef</em>: the name has to be
/// resolved as of a date, and <see cref="Start"/> is the only date under which it is true of this
/// stay rather than of the file.
/// </summary>
public sealed record ExportedStay(
    int ServiceId, string ServiceName, DateOnly Start, DateOnly End, int PeriodCount);

public enum StagePeriodShape
{
    /// <summary>Nothing published or recorded — the stage is owed, not served.</summary>
    None,

    /// <summary>The ordinary row: one période, one service.</summary>
    Single,

    /// <summary>Several périodes, one service, meeting end to end. Prints as one span.</summary>
    SingleServiceContiguous,

    /// <summary>
    /// Several périodes, one service, with worked days nobody served in between — a suspension, a
    /// délocalisation, or two attempts recorded on one assignment. ⚠ Prints as several spans: the
    /// merged one would claim days the student was not there.
    /// </summary>
    SingleServiceInterrupted,

    /// <summary>A real rotation: the group changed service between columns.</summary>
    MultiService,
}

public sealed record StagePeriodSummary(
    int PeriodCount,
    int ServiceCount,
    IReadOnlyList<ExportedStay> Stays,
    DateOnly? Start,
    DateOnly? End,
    int WorkingDays,
    int CalendarDays,
    StagePeriodShape Shape,
    IReadOnlyList<ExportedPeriod> Periods)
{
    public static readonly StagePeriodSummary Empty = new(
        0, 0, [], null, null, 0, 0, StagePeriodShape.None, []);

    /// <summary>The « Service(s) » cell: one name, or the itinerary in the order it was served.</summary>
    public string ServicesText => StagePeriodFolder.ServicesText(this);

    /// <summary>The « Période(s) » cell: one span per stay, joined.</summary>
    public string PeriodsText => StagePeriodFolder.PeriodsText(this);

    /// <summary>The « Découpage » cell — <see cref="Shape"/> in words, with its numbers.</summary>
    public string ShapeText => StagePeriodFolder.ShapeText(this);
}
