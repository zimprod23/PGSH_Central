using PGSH.Domain.Calendar;

namespace PGSH.Application.Stages.Export;

/// <summary>
/// Turns the planning créneaux one <c>ServicePeriod</c> was materialised from into what a document
/// prints for them.
///
/// <para><b>The question this answers.</b> A <c>SingleService</c> run is published as <b>one</b>
/// période spanning its whole run — right, because the student stands in one service and is marked
/// once — but the grid that produced it authored <c>kₛ</c> columns, each with its own window. The
/// périodes sheet showed « une période, 08/12/2026 – 07/03/2027 » and the three columns behind it
/// were nowhere in the file. Reported 2026-08-31: « on ne voit qu'une période alors qu'on en a
/// trois ». Both facts are true and the document has to state both.</para>
///
/// <para>⚠ <b>The count is a number in its own column, never only a string.</b> Same rule
/// <see cref="StagePeriodSummary.PeriodCount"/> follows: « montre-moi les stages publiés sur trois
/// créneaux » has to be a filter, not a reading exercise.</para>
///
/// <para>Only consecutive numbers merge, exactly as <c>GroupNumberRanges</c> does and for the same
/// reason: « P4-P6 » is a claim that P5 is in there too, so merging across a hole would describe a
/// column the run never occupied.</para>
///
/// <para>Pure — no store, no clock — like <see cref="StagePeriodFolder"/>.</para>
/// </summary>
public static class CoveredSlotFolder
{
    private const string RangeSeparator = ", ";

    public static CoveredSlotSummary Fold(
        IReadOnlyList<CoveredSlot> slots, WorkingDayCalendar calendar)
    {
        if (slots.Count == 0)
            return CoveredSlotSummary.None;

        var ordered = slots
            .DistinctBy(s => s.PeriodNumber)
            .OrderBy(s => s.PeriodNumber)
            .ToList();

        return new CoveredSlotSummary(
            ordered.Count,
            RangeText(ordered),
            DetailText(ordered, calendar),
            ordered);
    }

    private static string RangeText(IReadOnlyList<CoveredSlot> ordered)
    {
        var parts = new List<string>();
        var runStart = ordered[0];
        var runEnd = runStart;

        foreach (var slot in ordered.Skip(1))
        {
            if (slot.PeriodNumber == runEnd.PeriodNumber + 1)
            {
                runEnd = slot;
                continue;
            }

            parts.Add(Run(runStart, runEnd));
            runStart = runEnd = slot;
        }

        parts.Add(Run(runStart, runEnd));
        return string.Join(RangeSeparator, parts);
    }

    private static string Run(CoveredSlot start, CoveredSlot end) =>
        start.PeriodNumber == end.PeriodNumber ? start.Name : $"{start.Name}-{end.Name}";

    /// <summary>
    /// One line per créneau, with the window <em>it</em> covers and what that is worth in jours
    /// ouvrables — the numbers the folded période's own span cannot state.
    /// </summary>
    private static string DetailText(IReadOnlyList<CoveredSlot> ordered, WorkingDayCalendar calendar) =>
        string.Join('\n', ordered.Select(s => string.Join(" · ",
        [
            s.Name,
            StagePeriodFolder.Span(s.Start, s.End),
            $"{calendar.Count(s.Start, s.End)} j.o.",
        ])));
}

/// <summary>
/// One planning créneau, reduced to what the printing needs. <see cref="Name"/> prefers the label the
/// axis was authored with and falls back to <c>P{n}</c> — the axis in this base is labelled P1…P10,
/// and a créneau nobody labelled still has to be nameable.
/// </summary>
public sealed record CoveredSlot(int PeriodNumber, string? Label, DateOnly Start, DateOnly End)
{
    public string Name => string.IsNullOrWhiteSpace(Label) ? $"P{PeriodNumber}" : Label!.Trim();
}

/// <summary>
/// What a période's créneaux amount to: how many, which ones, and each one's own window.
/// <see cref="Count"/> is 0 for an ad-hoc période — imported history, a délocalisation, a
/// revalidation — which is the true answer for it and not a gap in the read.
/// </summary>
public sealed record CoveredSlotSummary(
    int Count,
    string RangeText,
    string DetailText,
    IReadOnlyList<CoveredSlot> Slots)
{
    public static readonly CoveredSlotSummary None = new(0, "", "", []);
}
