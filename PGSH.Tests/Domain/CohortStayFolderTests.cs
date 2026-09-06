using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// Folding a cohorte's cells into the stays a <c>ServicePeriod</c> is written for.
///
/// <para>The rule was private inside <c>SchedulePublisher</c> until « changement de groupe » had to
/// produce the périodes a cohorte's member holds. Written twice it would eventually disagree about
/// <see cref="StageRotationMode.SingleService"/> — and the disagreement is not cosmetic: a student
/// holding <i>kₛ</i> périodes where his classmates hold one is asked for <i>kₛ</i> marks and averages
/// differently from all of them.</para>
/// </summary>
public class CohortStayFolderTests
{
    private static CohortCell Cell(int id, int period, int service, int month) =>
        new(id, period, service, new DateOnly(2025, month, 1), new DateOnly(2025, month, 28));

    [Fact]
    public void Per_periode_gives_one_stay_per_cell()
    {
        var stays = CohortStayFolder.Fold(
            [Cell(1, 1, 10, 10), Cell(2, 2, 11, 11), Cell(3, 3, 12, 12)],
            StageRotationMode.PerPeriod);

        stays.Should().HaveCount(3);
        stays.Should().AllSatisfy(s => s.CellIds.Should().HaveCount(1));
        stays.Select(s => s.ServiceId).Should().Equal(10, 11, 12);
    }

    [Fact]
    public void A_single_service_run_of_consecutive_columns_is_one_stay_spanning_them()
    {
        var stays = CohortStayFolder.Fold(
            [Cell(1, 1, 10, 10), Cell(2, 2, 10, 11), Cell(3, 3, 10, 12)],
            StageRotationMode.SingleService);

        var stay = stays.Should().ContainSingle().Subject;
        stay.CellIds.Should().Equal(1, 2, 3);
        stay.LeadCellId.Should().Be(1, "the foreign key names the first cell of the run");
        stay.ServiceId.Should().Be(10);
        stay.StartDate.Should().Be(new DateOnly(2025, 10, 1));
        stay.EndDate.Should().Be(new DateOnly(2025, 12, 28));
    }

    /// <summary>
    /// ⚠ A cell edited by hand onto another service is two stays, not one période whose service is a
    /// lie for half its span.
    /// </summary>
    [Fact]
    public void A_change_of_service_breaks_the_run()
    {
        var stays = CohortStayFolder.Fold(
            [Cell(1, 1, 10, 10), Cell(2, 2, 99, 11), Cell(3, 3, 10, 12)],
            StageRotationMode.SingleService);

        stays.Select(s => s.ServiceId).Should().Equal(10, 99, 10);
        stays.Should().AllSatisfy(s => s.CellIds.Should().HaveCount(1));
    }

    /// <summary>
    /// A hole in the columns breaks it too: P1 and P3 in one service are two stays, because a single
    /// continuous période across them would claim the student was there in P2.
    /// </summary>
    [Fact]
    public void A_gap_in_the_columns_breaks_the_run()
    {
        var stays = CohortStayFolder.Fold(
            [Cell(1, 1, 10, 10), Cell(3, 3, 10, 12)],
            StageRotationMode.SingleService);

        stays.Should().HaveCount(2);
        stays[0].CellIds.Should().Equal(1);
        stays[1].CellIds.Should().Equal(3);
    }

    [Fact]
    public void The_cells_are_folded_in_column_order_whatever_order_they_arrive_in()
    {
        var stays = CohortStayFolder.Fold(
            [Cell(3, 3, 10, 12), Cell(1, 1, 10, 10), Cell(2, 2, 10, 11)],
            StageRotationMode.SingleService);

        stays.Should().ContainSingle().Which.CellIds.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void A_cohorte_with_no_published_cell_folds_to_nothing()
    {
        CohortStayFolder.Fold([], StageRotationMode.SingleService).Should().BeEmpty();
        CohortStayFolder.Fold([], StageRotationMode.PerPeriod).Should().BeEmpty();
    }
}
