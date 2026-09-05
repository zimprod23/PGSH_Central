using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// The order a stage's services are walked in. Pure, so the cases are exact rather than
/// approximately seeded — the same bargain as <c>PeriodAxis</c>, <c>RotationTiling</c> and
/// <c>StagePeriodFolder</c>.
///
/// <para>⚠ What makes this worth its own suite is that the rank is a <b>planning input</b>:
/// <c>RotationArranger</c> emits each service's block of the queue consecutively and the earliest
/// column takes phase 0, so the service ranked first receives the first run of group numbers in the
/// first période. A ranking that is silently completed, or that leaves two services sharing a
/// position, decides a promotion's year without anybody having said so.</para>
/// </summary>
public class ServiceRotationOrderTests
{
    [Fact]
    public void A_reorder_ranks_the_services_contiguously_in_the_order_asked_for()
    {
        var result = ServiceRotationOrder.Reorder([1, 2, 3], [3, 1, 2]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(3, 1, 2);
        ServiceRotationOrder.RanksFor(result.Value)
            .Should().BeEquivalentTo(new Dictionary<int, int> { [3] = 1, [1] = 2, [2] = 3 });
    }

    [Fact]
    public void A_list_that_leaves_a_service_out_is_refused_rather_than_completed()
    {
        // The likeliest cause is a page opened before somebody else authorised a service, not an
        // intention to leave it last — and appending it silently would state an order nobody
        // authored, in the one place whose whole purpose is that the order is authored.
        var result = ServiceRotationOrder.Reorder([1, 2, 3], [3, 1]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Stages.ServiceOrderNotAPermutation");
        result.Error.Description.Should().Contain("1 service(s) autorisé(s) n'y figurent pas");
    }

    [Fact]
    public void A_service_the_stage_no_longer_allows_is_refused()
    {
        var result = ServiceRotationOrder.Reorder([1, 2], [1, 2, 99]);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("n'appartiennent plus à la liste du stage");
    }

    [Fact]
    public void A_service_named_twice_is_refused()
    {
        // Not pedantry: two positions for one service means one of them belongs to nothing, so the
        // list can no longer say what any later position holds.
        var result = ServiceRotationOrder.Reorder([1, 2], [1, 1]);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("y figurent deux fois");
    }

    [Fact]
    public void The_refusal_names_every_cause_that_applies()
    {
        // Three causes, three different acts — reload the page, drop the stale id, remove the
        // duplicate. One flat "ordre invalide" sends the user to whichever they guess first.
        var result = ServiceRotationOrder.Reorder([1, 2, 3], [1, 1, 99]);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("n'y figurent pas")
              .And.Contain("n'appartiennent plus")
              .And.Contain("deux fois");
    }

    [Fact]
    public void An_empty_list_on_a_stage_that_allows_nothing_is_a_valid_empty_ranking()
    {
        var result = ServiceRotationOrder.Reorder([], []);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void A_newly_authorised_service_goes_last()
    {
        // It has no claim on a position somebody chose for the ones already there.
        ServiceRotationOrder.Append([5, 6], 7).Should().Equal(5, 6, 7);
    }

    [Fact]
    public void Appending_a_service_already_in_the_list_moves_nobody()
    {
        ServiceRotationOrder.Append([5, 6], 5).Should().Equal(5, 6);
    }

    [Fact]
    public void Removing_a_service_closes_the_hole_it_leaves()
    {
        // Ranks are positions, so a hole would make the number shown beside a service disagree with
        // the place it actually takes in the queue.
        ServiceRotationOrder.Without([5, 6, 7], 6).Should().Equal(5, 7);
        ServiceRotationOrder.RanksFor(ServiceRotationOrder.Without([5, 6, 7], 6))
            .Should().BeEquivalentTo(new Dictionary<int, int> { [5] = 1, [7] = 2 });
    }

    [Fact]
    public void Removing_a_service_that_is_not_there_changes_nothing()
    {
        ServiceRotationOrder.Without([5, 6], 99).Should().Equal(5, 6);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    public void A_ranked_row_sorts_on_its_rank(int rank, int expected) =>
        ServiceRotationOrder.SortKeyOf(rank).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void An_unranked_row_sorts_last_and_never_first(int rank)
    {
        // ⚠ The column defaults to 0. Sorting on the raw value would put a row inserted by a
        // corrective script *ahead* of every service somebody deliberately placed — and hand it the
        // first run of group numbers, which is the exact opposite of what an absent choice means.
        ServiceRotationOrder.SortKeyOf(rank).Should().Be(int.MaxValue);
    }
}
