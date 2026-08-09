namespace PGSH.Application.Stages.Repartition;

/// <summary>
/// One column of the répartition: a date window shared by every stage of the level.
/// </summary>
public sealed record PeriodWindow(int Index, DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// Builds the single date axis the whole level prints against, out of the windows its stages
/// actually declare.
///
/// The stages of a level do <b>not</b> all run the same period length. In the published 6th-year
/// répartition, ANES REA and URGS-TRAUMA change service every month while Chirurgie, Médecine,
/// Gynécologie and Pédiatrie keep theirs for two — ten monthly columns, with the two-month stages
/// holding the same groups across each pair. So the axis is the <b>finest</b> partition present:
/// a window that strictly contains another stage's window is a composite of it and is dropped,
/// leaving only the atoms. When every stage agrees (the 3rd-year case) nothing is dropped and the
/// axis is just those periods.
/// </summary>
public static class PeriodAxis
{
    public static IReadOnlyList<PeriodWindow> Build(IEnumerable<(DateOnly Start, DateOnly End)> windows)
    {
        var distinct = windows.Distinct().ToList();

        var atoms = distinct
            .Where(w => !distinct.Any(other => other != w && Contains(w, other)))
            .OrderBy(w => w.Start)
            .ThenBy(w => w.End)
            .ToList();

        return atoms
            .Select((w, i) => new PeriodWindow(i + 1, w.Start, w.End))
            .ToList();
    }

    /// <summary>
    /// The columns <paramref name="window"/> occupies, by <b>midpoint containment</b> rather than by
    /// any overlap: a stage owns a column when the column's middle day falls inside its own window.
    /// Bare overlap would let a window that spills a few days past a boundary claim the whole next
    /// column and overwrite the stage that really runs there; the midpoint rule keeps a misaligned
    /// window in the columns it actually covers.
    /// </summary>
    public static IEnumerable<PeriodWindow> ColumnsCovered(
        IReadOnlyList<PeriodWindow> axis, DateOnly start, DateOnly end)
    {
        foreach (var column in axis)
        {
            var midpoint = column.StartDate.AddDays(
                (column.EndDate.DayNumber - column.StartDate.DayNumber) / 2);

            if (midpoint >= start && midpoint <= end)
                yield return column;
        }
    }

    private static bool Contains((DateOnly Start, DateOnly End) outer, (DateOnly Start, DateOnly End) inner) =>
        inner.Start >= outer.Start && inner.End <= outer.End;
}
