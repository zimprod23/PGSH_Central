using FluentAssertions;
using PGSH.Application.Stages.Repartition;
using Xunit;

namespace PGSH.Tests.Application;

// The two pure halves of the répartition pivot, checked against the published document itself
// (example_stage_assignement/Med3.png, Med6.png) rather than against invented shapes.
public class GroupNumberRangesTests
{
    [Fact]
    public void A_contiguous_run_collapses_to_a_range()
    {
        // Med3, Chirurgie / HMIMV: Chirurgie A, période 1.
        GroupNumberRanges.Format([47, 48, 49, 50]).Should().Be("47-50");
    }

    [Fact]
    public void A_single_group_prints_bare()
    {
        // Med6, Chirurgie / HMIMV: Urologie, période 3.
        GroupNumberRanges.Format([27]).Should().Be("27");
    }

    [Fact]
    public void A_hole_splits_the_cell_instead_of_being_swallowed()
    {
        // Merging across the hole would promise groups 49 to a service they are not in.
        GroupNumberRanges.Format([47, 48, 50]).Should().Be("47-48, 50");
    }

    [Fact]
    public void Several_runs_each_keep_their_own_range()
    {
        GroupNumberRanges.Format([1, 2, 3, 7, 9, 10]).Should().Be("1-3, 7, 9-10");
    }

    [Fact]
    public void Unordered_and_duplicated_input_prints_the_same_cell()
    {
        // Cells arrive grouped by slot, in no particular order, and a cohort can be listed twice.
        GroupNumberRanges.Format([50, 47, 49, 48, 48]).Should().Be("47-50");
    }

    [Fact]
    public void No_groups_prints_nothing()
    {
        GroupNumberRanges.Format([]).Should().BeEmpty();
    }
}

public class PeriodAxisTests
{
    private static readonly (DateOnly, DateOnly)[] FourEqualPeriods =
    [
        (new DateOnly(2025, 11, 3),  new DateOnly(2025, 12, 17)),
        (new DateOnly(2025, 12, 18), new DateOnly(2026, 3, 17)),
        (new DateOnly(2026, 3, 18),  new DateOnly(2026, 5, 3)),
        (new DateOnly(2026, 5, 4),   new DateOnly(2026, 6, 18)),
    ];

    [Fact]
    public void Stages_that_agree_on_their_periods_give_exactly_those_columns()
    {
        // Med3: every stage runs the same four periods, so nothing is composite.
        var axis = PeriodAxis.Build(FourEqualPeriods.Concat(FourEqualPeriods));

        axis.Should().HaveCount(4);
        axis.Select(c => c.Index).Should().Equal(1, 2, 3, 4);
        axis[0].StartDate.Should().Be(new DateOnly(2025, 11, 3));
        axis[3].EndDate.Should().Be(new DateOnly(2026, 6, 18));
    }

    [Fact]
    public void A_two_month_period_is_dropped_in_favour_of_the_monthly_ones_it_contains()
    {
        // Med6: ANES REA changes service monthly, Chirurgie every two months. The axis is monthly.
        var monthly = new[]
        {
            (new DateOnly(2025, 11, 3), new DateOnly(2025, 12, 2)),
            (new DateOnly(2025, 12, 3), new DateOnly(2026, 1, 2)),
        };
        var bimonthly = (new DateOnly(2025, 11, 3), new DateOnly(2026, 1, 2));

        var axis = PeriodAxis.Build(monthly.Append(bimonthly));

        axis.Should().HaveCount(2);
        axis[0].EndDate.Should().Be(new DateOnly(2025, 12, 2));
        axis[1].EndDate.Should().Be(new DateOnly(2026, 1, 2));
    }

    [Fact]
    public void A_period_covers_every_column_whose_midpoint_falls_inside_it()
    {
        var axis = PeriodAxis.Build(
        [
            (new DateOnly(2025, 11, 3), new DateOnly(2025, 12, 2)),
            (new DateOnly(2025, 12, 3), new DateOnly(2026, 1, 2)),
        ]);

        PeriodAxis.ColumnsCovered(axis, new DateOnly(2025, 11, 3), new DateOnly(2026, 1, 2))
            .Select(c => c.Index).Should().Equal(1, 2);

        PeriodAxis.ColumnsCovered(axis, new DateOnly(2025, 11, 3), new DateOnly(2025, 12, 2))
            .Select(c => c.Index).Should().Equal(1);
    }

    [Fact]
    public void A_period_spilling_a_few_days_past_a_boundary_does_not_claim_the_next_column()
    {
        // Bare overlap would hand column 2 to a stage that only reaches into its first fortnight,
        // overwriting whichever stage really runs there.
        var axis = PeriodAxis.Build(
        [
            (new DateOnly(2025, 11, 3), new DateOnly(2025, 12, 2)),
            (new DateOnly(2025, 12, 3), new DateOnly(2026, 1, 2)),
        ]);

        PeriodAxis.ColumnsCovered(axis, new DateOnly(2025, 11, 3), new DateOnly(2025, 12, 10))
            .Select(c => c.Index).Should().Equal(1);
    }
}
