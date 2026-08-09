namespace PGSH.Application.Stages.Repartition;

/// <summary>
/// A period number that several stages of the level declare on <em>different</em> windows.
/// </summary>
/// <param name="Windows">The distinct windows found, as <c>"01/10 → 31/10 (Médecine)"</c>.</param>
public sealed record AxisDisagreement(int PeriodNumber, IReadOnlyList<string> Windows);

/// <summary>
/// Finds period numbers whose stages do not agree on the dates.
///
/// <para><b>Why this is needed at all.</b> <c>StageSlot</c> is keyed (stage, year, period number), so
/// Médecine P1 and Chirurgie P1 are two independent rows with independent dates. Nothing in the schema
/// says they are the same window — the axis is <i>derived</i> from dates, never declared. And neither
/// guard notices: <c>SlotOverlapGuard</c> is per-stage (which is what makes the crossover authorable
/// in the first place), and <c>GroupScheduleConflictGuard</c> only fires on a group actually
/// double-booked, which a crossover never is.</para>
///
/// <para>So a mistyped date silently misaligns the published table. A small drift is worse than a large
/// one: where one window strictly contains another, <c>PeriodAxis</c> treats the outer as a composite
/// and drops it, absorbing the mistake without trace. This is what makes it visible.</para>
///
/// <para>⚠ A disagreement is <b>not</b> an error. In the published 6th-year table Chirurgie's P1 runs
/// two months while ANES REA's P1 runs one — legitimately the same number on different windows. It is
/// reported so a human can tell the two apart, which is the one thing the code cannot.</para>
/// </summary>
public static class PeriodAxisDiagnostics
{
    public static IReadOnlyList<AxisDisagreement> Find(
        IEnumerable<(int PeriodNumber, string StageName, DateOnly Start, DateOnly End)> slots) =>
        slots
            .GroupBy(s => s.PeriodNumber)
            .Where(g => g.Select(s => (s.Start, s.End)).Distinct().Count() > 1)
            .OrderBy(g => g.Key)
            .Select(g => new AxisDisagreement(
                g.Key,
                g.GroupBy(s => (s.Start, s.End))
                    .OrderBy(w => w.Key.Start)
                    .Select(w => $"{w.Key.Start:dd/MM} → {w.Key.End:dd/MM} "
                               + $"({string.Join(", ", w.Select(s => s.StageName).Distinct().Order())})")
                    .ToList()))
            .ToList();
}
