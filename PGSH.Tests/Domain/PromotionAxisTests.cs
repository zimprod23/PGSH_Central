using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// The arithmetic behind « combien d'étudiants sont dans ce stage en même temps », exercised where it
/// is exact — no store, no fixture.
/// </summary>
public class PromotionAxisTests
{
    /// <summary>
    /// The 3ᵉ MED's real catalogue: two 30-day stages and six 15-day ones. ⚠ The column is the
    /// <b>gcd</b>, which is what lets stages of two lengths share one axis — a 30-day stage is
    /// exactly two columns of it, never a wider column of its own.
    /// </summary>
    [Fact]
    public void The_column_is_the_gcd_and_the_timeline_is_the_sum_of_the_weights()
    {
        var axis = PromotionAxis.Lay([
            (1, 30), (2, 30), (3, 15), (4, 15), (5, 15), (6, 15), (7, 15), (8, 15),
        ]);

        axis.ColumnDays.Should().Be(15);
        axis.Timeline.Should().Be(10);
        axis.Weights.Should().Contain(w => w.StageId == 1 && w.Periods == 2);
        axis.Weights.Should().Contain(w => w.StageId == 3 && w.Periods == 1);
    }

    /// <summary>Durations that share no smaller factor still tile — one column per day if it comes to that.</summary>
    [Fact]
    public void Durations_with_no_common_factor_still_lay_an_axis()
    {
        var axis = PromotionAxis.Lay([(1, 20), (2, 21)]);

        axis.ColumnDays.Should().Be(1);
        axis.Timeline.Should().Be(41);
    }

    /// <summary>
    /// ⚠ <b>Rounded up, and it matters.</b> 933 over ten columns is 94 in the fullest one. Rounding
    /// down would print 93 and turn a shortfall of 14 places into one of 13 — a deficit understated
    /// is the reading that lets a publication be attempted.
    /// </summary>
    [Fact]
    public void The_simultaneous_slice_rounds_up()
    {
        PromotionAxis.SimultaneousStudents(933, periods: 1, timeline: 10).Should().Be(94);
        PromotionAxis.SimultaneousStudents(933, periods: 2, timeline: 10).Should().Be(187);
        PromotionAxis.SimultaneousStudents(930, periods: 1, timeline: 10).Should().Be(93);
    }

    /// <summary>Nothing to divide, and nothing pretending otherwise.</summary>
    [Theory]
    [InlineData(0, 1, 10)]
    [InlineData(100, 0, 10)]
    [InlineData(100, 1, 0)]
    public void Nothing_to_place_is_zero_not_a_division(int students, int periods, int timeline) =>
        PromotionAxis.SimultaneousStudents(students, periods, timeline).Should().Be(0);

    /// <summary>
    /// ⚠ A stage with no duration sits on no axis, and is <b>reported</b> rather than dropped: left
    /// out silently it would shrink Σdurées and make every other stage's share look larger, so a
    /// promotion could read as fitting because one of its stages was invisible.
    /// </summary>
    [Fact]
    public void A_stage_without_a_duration_is_named_not_swallowed()
    {
        var axis = PromotionAxis.Lay([(1, 30), (2, 0), (3, -5)]);

        axis.Timeline.Should().Be(1, "only the 30-day stage carries weight");
        axis.Weights.Should().ContainSingle().Which.StageId.Should().Be(1);
        axis.Unweighted.Should().BeEquivalentTo([2, 3]);
    }

    /// <summary>A promotion whose every stage lacks a duration has no axis at all — and says so.</summary>
    [Fact]
    public void An_axis_of_nothing_is_empty_rather_than_a_division_by_zero()
    {
        var axis = PromotionAxis.Lay([(1, 0)]);

        axis.Timeline.Should().Be(0);
        axis.ColumnDays.Should().Be(0);
        axis.Unweighted.Should().ContainSingle();
    }
}
