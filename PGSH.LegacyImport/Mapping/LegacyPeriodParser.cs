using System.Globalization;
using System.Text.RegularExpressions;

namespace PGSH.LegacyImport.Mapping;

/// <summary>
/// Reads the rotation window out of `AffectStage.PER1`/`PER2`, which Access stores as free text.
///
/// 824 of the 104,924 rows carry a second window inside <c>PER2</c>, e.g.
/// <c>"31/05/2019 &amp; de: 25/06/2019 à:12/07/2019"</c> — a rotation interrupted and resumed, which
/// the legacy app could not express so somebody typed it into the string. PGSH models it natively as
/// two <c>ServicePeriod</c>s, so every date in the pair is collected and paired up in order.
/// </summary>
public static class LegacyPeriodParser
{
    private static readonly Regex DatePattern = new(@"\b(\d{2})/(\d{2})/(\d{4})\b", RegexOptions.Compiled);

    public static LegacyPeriodParseResult Parse(string? per1, string? per2)
    {
        var dates = new List<DateOnly>();
        Collect(per1, dates);
        Collect(per2, dates);

        if (dates.Count < 2)
            return new LegacyPeriodParseResult([], Unreadable: true);

        var windows = new List<LegacyWindow>();
        for (int i = 0; i + 1 < dates.Count; i += 2)
        {
            var start = dates[i];
            var end = dates[i + 1];

            // A window running backwards is a typo, not a rotation. Keep the row by collapsing it to
            // a single day rather than dropping a student's stage over one bad cell.
            if (end < start) end = start;
            windows.Add(new LegacyWindow(start, end));
        }

        // An odd trailing date describes a window with no end — reported, never guessed at.
        bool trailing = dates.Count % 2 != 0;

        return new LegacyPeriodParseResult(windows, Unreadable: false, DanglingDate: trailing);
    }

    private static void Collect(string? text, List<DateOnly> into)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (Match m in DatePattern.Matches(text))
        {
            if (DateOnly.TryParseExact(m.Value, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                into.Add(parsed);
        }
    }
}

public sealed record LegacyWindow(DateOnly Start, DateOnly End);

public sealed record LegacyPeriodParseResult(
    IReadOnlyList<LegacyWindow> Windows,
    bool Unreadable,
    bool DanglingDate = false)
{
    public bool IsSplit => Windows.Count > 1;
}
