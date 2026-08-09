namespace PGSH.Application.Stages.Repartition;

/// <summary>
/// Collapses the group numbers of one cell into the printed form: <c>47,48,49,50 → "47-50"</c>,
/// <c>47,48,50 → "47-48, 50"</c>, <c>27 → "27"</c>.
///
/// Only consecutive runs merge. The published répartition leaves non-contiguous runs separate — a
/// cell reading "47-50" is a promise that groups 48 and 49 are in that service too, so merging
/// across a hole would misdirect two whole groups of students.
/// </summary>
public static class GroupNumberRanges
{
    public static string Format(IEnumerable<int> groupNumbers)
    {
        var ordered = groupNumbers.Distinct().Order().ToList();
        if (ordered.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        int runStart = ordered[0];
        int runEnd = runStart;

        foreach (int number in ordered.Skip(1))
        {
            if (number == runEnd + 1)
            {
                runEnd = number;
                continue;
            }

            parts.Add(Run(runStart, runEnd));
            runStart = runEnd = number;
        }

        parts.Add(Run(runStart, runEnd));
        return string.Join(", ", parts);
    }

    private static string Run(int start, int end) => start == end ? $"{start}" : $"{start}-{end}";
}
