using FluentAssertions;
using PGSH.Application.Stages.Export;
using PGSH.Domain.Calendar;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// What a document prints for the planning créneaux behind one <c>ServicePeriod</c>.
///
/// <para>Pure, like <c>StagePeriodFolderTests</c>: the folding takes no store and no clock, so the
/// cases are exact rather than approximately seeded.</para>
/// </summary>
public class CoveredSlotFolderTests
{
    private static readonly WorkingDayCalendar Calendar = WorkingDayCalendar.Build([]);

    private static CoveredSlot Slot(int number, int month, string? label = null) =>
        new(number, label, new DateOnly(2026, month, 1), new DateOnly(2026, month, 28));

    /// <summary>
    /// The live case: 5MED Gynécologie Obstétrique publishes one période per student covering three
    /// consecutive columns of the axis.
    /// </summary>
    [Fact]
    public void A_run_of_consecutive_creneaux_collapses_to_one_range_and_still_counts_three()
    {
        var summary = CoveredSlotFolder.Fold([Slot(4, 1), Slot(5, 2), Slot(6, 3)], Calendar);

        summary.Count.Should().Be(3, "the number is a column of its own — « trois créneaux » has to be a filter");
        summary.RangeText.Should().Be("P4-P6");
    }

    /// <summary>
    /// ⚠ Only consecutive numbers merge, exactly as <c>GroupNumberRanges</c> does: « P1-P3 » is a
    /// claim that P2 is in the run too, and a run that skipped it never occupied that column.
    /// </summary>
    [Fact]
    public void A_hole_is_never_merged_across()
    {
        CoveredSlotFolder.Fold([Slot(1, 1), Slot(3, 3), Slot(4, 4)], Calendar)
            .RangeText.Should().Be("P1, P3-P4");
    }

    [Fact]
    public void One_creneau_prints_its_own_name()
    {
        var summary = CoveredSlotFolder.Fold([Slot(2, 2)], Calendar);

        summary.Count.Should().Be(1);
        summary.RangeText.Should().Be("P2");
    }

    /// <summary>An ad-hoc période — imported history, a délocalisation, a revalidation — came from no
    /// grid at all, and « 0 créneau » is the true answer for it rather than a gap in the read.</summary>
    [Fact]
    public void A_periode_that_came_from_no_grid_folds_to_nothing()
    {
        var summary = CoveredSlotFolder.Fold([], Calendar);

        summary.Should().BeSameAs(CoveredSlotSummary.None);
        summary.Count.Should().Be(0);
        summary.RangeText.Should().BeEmpty();
    }

    /// <summary>The axis in this base is labelled P1…P10; a créneau nobody labelled still has to be
    /// nameable, which is what the <c>P{n}</c> fallback is for.</summary>
    [Fact]
    public void An_authored_label_wins_over_the_fallback_name()
    {
        CoveredSlotFolder.Fold([Slot(1, 1, "S1"), Slot(2, 2, "S2")], Calendar)
            .RangeText.Should().Be("S1-S2");
    }

    /// <summary>
    /// ⚠ The detail is what the folded période's own span cannot say: three windows, each with its
    /// own worked-day count. It is the answer to « on ne voit qu'une période alors qu'on en a trois ».
    /// </summary>
    [Fact]
    public void The_detail_gives_each_creneau_its_own_window()
    {
        var summary = CoveredSlotFolder.Fold([Slot(4, 1), Slot(5, 2), Slot(6, 3)], Calendar);

        summary.DetailText.Split('\n').Should().HaveCount(3);
        summary.DetailText.Should().Contain("P4 · 01/01/2026 – 28/01/2026");
        summary.DetailText.Should().Contain("P6 · 01/03/2026 – 28/03/2026");
        summary.DetailText.Should().Contain("j.o.");
    }

    /// <summary>Two coverage rows naming the same column is one column of the axis, not two.</summary>
    [Fact]
    public void The_same_creneau_twice_is_counted_once()
    {
        CoveredSlotFolder.Fold([Slot(1, 1), Slot(1, 1), Slot(2, 2)], Calendar)
            .Count.Should().Be(2);
    }

    /// <summary>Coverage rows arrive in whatever order the read produced; the printed range is the
    /// axis's order, not the query's.</summary>
    [Fact]
    public void The_creneaux_are_ordered_by_the_axis_not_by_the_read()
    {
        CoveredSlotFolder.Fold([Slot(6, 3), Slot(4, 1), Slot(5, 2)], Calendar)
            .RangeText.Should().Be("P4-P6");
    }
}
