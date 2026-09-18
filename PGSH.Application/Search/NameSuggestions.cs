namespace PGSH.Application.Search;

/// <summary>
/// « Vouliez-vous dire… » — the catalogue entries closest to a name a spreadsheet got slightly wrong.
/// </summary>
/// <remarks>
/// <para><b>Why suggest rather than accept.</b> A service name in an affectation sheet decides where a
/// student physically stands for a month, and this faculty runs services whose names differ by one
/// character — « Médecine A » and « Médecine B », « Chirurgie 1 » and « Chirurgie 2 ». Matching those
/// loosely would send a cohorte to the wrong hospital <i>silently</i>, which is the one failure this
/// import is built to refuse: one bad line refuses the whole file precisely so that nothing is
/// half-written on a guess. So the match stays <b>exact</b> (after folding case and accents) and the
/// tolerance is spent on the <i>report</i> instead.</para>
///
/// <para><b>Why suggest at all.</b> Without it, « Aucun service ne porte ce nom » on line 412 of a
/// 900-line file leaves the operator to find the difference between what they typed and one of 148
/// catalogue entries by eye. The refusal was correct and useless. Naming the two or three nearest
/// entries turns it into a one-second correction.</para>
///
/// <para>⚠ <b>The distance is bounded on purpose.</b> A suggestion is only offered when the typed name
/// is genuinely close, so an operator who types something entirely different is told plainly that
/// nothing matches rather than being handed three unrelated services — a wrong suggestion beside a
/// correct refusal is worse than no suggestion, because it invites the operator to accept it.</para>
///
/// <para>Pure — no store, no clock — like <c>CohortStayFolder</c> and <c>StageScoring</c>, so its
/// boundary cases are exact rather than sampled.</para>
/// </remarks>
public static class NameSuggestions
{
    /// <summary>How many to offer. More than three reads as a list to search rather than an answer.</summary>
    private const int MaxSuggestions = 3;

    /// <summary>
    /// The entries of <paramref name="candidates"/> closest to <paramref name="typed"/>, nearest
    /// first, or empty when none is close enough to be worth naming.
    /// </summary>
    public static IReadOnlyList<string> Nearest(string? typed, IEnumerable<string?> candidates)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return [];

        string folded = SearchTerms.Fold(typed);

        // ⚠ Proportional, not a fixed number of edits. One wrong character in « ORL » is a different
        // word; one wrong character in « Chirurgie viscérale » is a typo. A fixed threshold has to
        // choose which of those two it gets wrong.
        int budget = Math.Max(1, folded.Length / 4);

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => (Name: candidate!, Distance: Distance(folded, SearchTerms.Fold(candidate!), budget)))
            .Where(match => match.Distance <= budget)
            .OrderBy(match => match.Distance)
            .ThenBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
            .Select(match => match.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .ToList();
    }

    /// <summary>
    /// That list as the half-sentence a refusal appends, or an empty string when there is nothing to
    /// suggest.
    /// </summary>
    /// <remarks>
    /// ⚠ Returns « » rather than a sentence saying there is no suggestion. A line that announces its
    /// own emptiness on every unmatched row is noise, and the refusal it follows already says what is
    /// wrong.
    /// </remarks>
    public static string Hint(string? typed, IEnumerable<string?> candidates)
    {
        var nearest = Nearest(typed, candidates);

        return nearest.Count == 0
            ? ""
            : $" Vouliez-vous dire {string.Join(", ", nearest.Select(n => $"« {n} »"))} ?";
    }

    /// <summary>
    /// Levenshtein distance, abandoned as soon as it exceeds <paramref name="budget"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ The early exit is not only a speed matter: it is what keeps a 900-line file times a 148-entry
    /// catalogue from becoming a visible pause on a screen whose whole job is to answer quickly.
    /// </remarks>
    private static int Distance(string left, string right, int budget)
    {
        if (Math.Abs(left.Length - right.Length) > budget)
            return budget + 1;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (int j = 0; j <= right.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            int rowBest = current[0];

            for (int j = 1; j <= right.Length; j++)
            {
                int cost = left[i - 1] == right[j - 1] ? 0 : 1;

                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                rowBest = Math.Min(rowBest, current[j]);
            }

            if (rowBest > budget)
                return budget + 1;

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
