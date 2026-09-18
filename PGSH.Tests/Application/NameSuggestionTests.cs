using FluentAssertions;
using PGSH.Application.Search;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Vouliez-vous dire… » on a service or stage name a spreadsheet got slightly wrong.
/// </summary>
/// <remarks>
/// ⚠ <b>The pair that matters is « suggests a typo » and « suggests nothing for a real difference ».</b>
/// A suggestion offered beside a correct refusal is worse than none, because it invites the operator
/// to accept it — and two services of this faculty differ by a single character, so the matching
/// itself stays exact and only the <i>report</i> is tolerant.
/// </remarks>
public class NameSuggestionTests
{
    private static readonly string[] Catalogue =
    [
        "Chirurgie viscérale", "Chirurgie thoracique", "Médecine A", "Médecine B",
        "Cardiologie", "ORL", "Pédiatrie",
    ];

    [Theory]
    [InlineData("Chirurgie visceral", "Chirurgie viscérale")]   // accent + one letter
    [InlineData("CHIRURGIE VISCERALE", "Chirurgie viscérale")]  // case + accents only
    [InlineData("Cardiologi", "Cardiologie")]                   // a dropped character
    [InlineData("Pediatrie", "Pédiatrie")]                      // accent alone
    public void A_near_miss_is_named(string typed, string expected)
    {
        NameSuggestions.Nearest(typed, Catalogue).Should().Contain(expected);
    }

    /// <summary>
    /// ⚠ <b>The control, and the one that protects the operator.</b> « Médecine A » and « Médecine B »
    /// differ by one character and are different places. The import must never quietly resolve one to
    /// the other — and here, asked about a name that <i>is</i> in the catalogue, the suggestion list is
    /// not what decides anything: the exact match already did.
    /// </summary>
    [Fact]
    public void Two_services_one_character_apart_are_both_offered_never_chosen()
    {
        var nearest = NameSuggestions.Nearest("Médecine C", Catalogue);

        nearest.Should().Contain("Médecine A").And.Contain("Médecine B",
            "both are equally close, so naming only one would be a guess dressed as an answer");
    }

    /// <summary>A name nothing resembles gets no suggestion at all, rather than three unrelated ones.</summary>
    [Fact]
    public void A_name_nothing_resembles_is_not_guessed_at()
    {
        NameSuggestions.Nearest("Radiologie interventionnelle", Catalogue).Should().BeEmpty();
    }

    /// <summary>
    /// ⚠ A short name has almost no budget: one wrong character in « ORL » is a different word, while
    /// one wrong character in « Chirurgie thoracique » is a typo. A fixed edit threshold has to get one
    /// of those two wrong, which is why the budget is proportional.
    /// </summary>
    [Fact]
    public void A_short_name_is_judged_more_strictly_than_a_long_one()
    {
        NameSuggestions.Nearest("OR", Catalogue).Should().Contain("ORL");
        NameSuggestions.Nearest("XYZ", Catalogue).Should().BeEmpty();
    }

    [Fact]
    public void An_empty_name_suggests_nothing()
    {
        NameSuggestions.Nearest("", Catalogue).Should().BeEmpty();
        NameSuggestions.Nearest(null, Catalogue).Should().BeEmpty();
    }

    /// <summary>
    /// ⚠ The hint is « » and not a sentence when there is nothing to say. A line announcing its own
    /// emptiness on every unmatched row is noise, and the refusal it follows already says what is
    /// wrong.
    /// </summary>
    [Fact]
    public void The_hint_is_silent_when_there_is_nothing_to_suggest()
    {
        NameSuggestions.Hint("Radiologie interventionnelle", Catalogue).Should().BeEmpty();
        NameSuggestions.Hint("Cardiologi", Catalogue).Should().Contain("Cardiologie");
    }

    /// <summary>At most three: more reads as a list to search rather than an answer.</summary>
    [Fact]
    public void At_most_three_are_offered()
    {
        string[] many = [.. Enumerable.Range(1, 20).Select(i => $"Service {i}")];

        NameSuggestions.Nearest("Service", many).Should().HaveCountLessThanOrEqualTo(3);
    }
}
