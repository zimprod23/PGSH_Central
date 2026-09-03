using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// The two pure verdicts behind « quel groupe va déjà là ? » and « cet hôpital peut-il l'accueillir ? ».
///
/// <para>Both exist for the same reason and both are tested the same way: each has a blank that means
/// two different things, and each has a state a naive reading collapses into the reassuring one.</para>
/// </summary>
public class RosterHospitalPlacementTests
{
    /// <summary>
    /// ⚠ <b>The case the type exists for.</b> « Toutes ses cellules sont au HMIMV » is vacuously true
    /// of a roster with no cell, so a roster nobody has arranged would be returned as the strongest
    /// possible match — the most confident answer in the promotion, drawn from a total absence of
    /// evidence. It is also the state of every roster in the live base, which holds 0 cells.
    /// </summary>
    [Fact]
    public void A_roster_with_no_cell_is_unplaced_never_entirely_anywhere()
    {
        RosterHospitalPlacementTest.Of(cells: 0, cellsAtHospital: 0)
            .Should().Be(RosterHospitalPlacement.Unplaced);
    }

    [Fact]
    public void Every_cell_at_the_hospital_is_entire()
    {
        RosterHospitalPlacementTest.Of(cells: 6, cellsAtHospital: 6)
            .Should().Be(RosterHospitalPlacement.Entire);
    }

    /// <summary>
    /// One cell elsewhere is enough to disqualify: « il y va aussi » is not the request that was made.
    /// </summary>
    [Fact]
    public void One_cell_elsewhere_makes_it_partial()
    {
        RosterHospitalPlacementTest.Of(cells: 6, cellsAtHospital: 5)
            .Should().Be(RosterHospitalPlacement.Partial);
    }

    [Fact]
    public void Placed_with_nothing_at_the_hospital_is_elsewhere_not_unplaced()
    {
        RosterHospitalPlacementTest.Of(cells: 6, cellsAtHospital: 0)
            .Should().Be(RosterHospitalPlacement.Elsewhere);
    }

    /// <summary>
    /// <see cref="RosterHospitalPlacement.Unplaced"/> outranks everything: with no cell there is no
    /// hospital fact to state at all, whatever the second number claims.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 3)]
    [InlineData(-1, 0)]
    public void Absence_of_cells_outranks_the_hospital_count(int cells, int atHospital)
    {
        RosterHospitalPlacementTest.Of(cells, atHospital)
            .Should().Be(RosterHospitalPlacement.Unplaced);
    }

    /// <summary>
    /// One roster, one cell — the smallest placement there is, and the one an off-by-one in the
    /// « at least one » guard would classify as <see cref="RosterHospitalPlacement.Unplaced"/>.
    /// </summary>
    [Fact]
    public void A_single_cell_at_the_hospital_is_already_entire()
    {
        RosterHospitalPlacementTest.Of(cells: 1, cellsAtHospital: 1)
            .Should().Be(RosterHospitalPlacement.Entire);
    }
}

public class StageHospitalCoverageTests
{
    /// <summary>
    /// ⚠ <b>The distinction the type exists for.</b> An empty allowed-services list is not enforced
    /// by <c>SetCohortSlotAssignmentCommandHandler</c>, so such a stage is open to <i>every</i>
    /// service — the blank means « personne n'a saisi la liste », not « cet hôpital est exclu ». Read
    /// as the second it reports a refusal no data supports, and sends the user to change hospital
    /// instead of to author the list. Three stages of the live catalogue are in this state.
    /// </summary>
    [Fact]
    public void No_authorised_service_is_not_a_refusal_by_this_hospital()
    {
        StageHospitalCoverageTest.Of(allowedServices: 0, servicesAtHospital: 0)
            .Should().Be(StageHospitalCoverage.NoServicesAuthored);
    }

    /// <summary>
    /// 5ᵉ année Santé Publique, measured on the live catalogue 2026-09-03: one authorised service,
    /// and it is not at the military hospital. This single row is what makes « tout au militaire »
    /// impossible for that promotion, and the whole reason the check happens before the promise.
    /// </summary>
    [Fact]
    public void Services_authorised_but_none_here_is_not_covered()
    {
        StageHospitalCoverageTest.Of(allowedServices: 1, servicesAtHospital: 0)
            .Should().Be(StageHospitalCoverage.NotAtThisHospital);
    }

    [Fact]
    public void One_authorised_service_here_is_enough()
    {
        StageHospitalCoverageTest.Of(allowedServices: 12, servicesAtHospital: 1)
            .Should().Be(StageHospitalCoverage.Covered);
    }

    /// <summary>
    /// The three states partition every pair a subset count can produce — no input falls outside
    /// them, which is the property that lets a caller switch on the verdict alone.
    /// </summary>
    [Theory]
    [InlineData(0, 0, StageHospitalCoverage.NoServicesAuthored)]
    [InlineData(1, 0, StageHospitalCoverage.NotAtThisHospital)]
    [InlineData(1, 1, StageHospitalCoverage.Covered)]
    [InlineData(35, 6, StageHospitalCoverage.Covered)]
    public void The_three_states_partition_the_inputs(
        int allowed, int here, StageHospitalCoverage expected)
    {
        StageHospitalCoverageTest.Of(allowed, here).Should().Be(expected);
    }
}
