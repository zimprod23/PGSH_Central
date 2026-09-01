using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// Where a rotation stands, asked once and answered the same way everywhere.
///
/// <para>The class exists because the rule was being restated as a raw boolean triple wherever it
/// was needed — <c>IsStarted &amp;&amp; !IsComplete &amp;&amp; !IsInterrupted</c> in four files, its
/// not-started twin in two — so what these tests really protect is that there is now exactly one
/// statement of it, and that the three forms it is available in (the EF expression, the entity
/// overload, the projection overload) cannot say different things.</para>
/// </summary>
public class ServicePeriodLifecycleTests
{
    /// <summary>Every combination of the four facts a state is decided from — 16 rows, no sampling.</summary>
    public static TheoryData<bool, bool, bool, bool> AllFlagCombinations()
    {
        var data = new TheoryData<bool, bool, bool, bool>();
        foreach (bool started in new[] { false, true })
            foreach (bool complete in new[] { false, true })
                foreach (bool interrupted in new[] { false, true })
                    foreach (bool evaluated in new[] { false, true })
                        data.Add(started, complete, interrupted, evaluated);
        return data;
    }

    private static ServicePeriod Period(bool started, bool complete, bool interrupted, bool evaluated) =>
        new()
        {
            Id = Guid.NewGuid(),
            IsStarted = started,
            IsComplete = complete,
            IsInterrupted = interrupted,
            Evaluation = evaluated
                ? new ServiceEvaluation { Mode = EvaluationMode.Numeric, TotalScore = 12m }
                : null,
        };

    // ⚠ The property the whole design rests on. Four filters that merely look useful would let a row
    // sit in two slices (counted twice) or in none (invisible, and counted nowhere) — and the second
    // is the failure that cannot be seen from any screen. Settled is written as the complement of the
    // other three precisely so this holds for combinations the lifecycle cannot even produce.
    [Theory]
    [MemberData(nameof(AllFlagCombinations))]
    public void The_four_states_partition_every_combination_of_flags(
        bool started, bool complete, bool interrupted, bool evaluated)
    {
        var period = Period(started, complete, interrupted, evaluated);

        var matching = Enum.GetValues<ServicePeriodState>()
            .Where(state => ServicePeriodLifecycle.Predicate(state).Compile()(period))
            .ToList();

        matching.Should().ContainSingle(
            "every period belongs to exactly one state — never two, and never none");
    }

    // The three forms are one rule or they are three rules. The compiled delegates come from the
    // expressions by construction; the projection overload is hand-written and is the one that could
    // drift, so it is pinned against them here.
    [Theory]
    [MemberData(nameof(AllFlagCombinations))]
    public void The_projection_overload_agrees_with_the_store_side_predicates(
        bool started, bool complete, bool interrupted, bool evaluated)
    {
        var period = Period(started, complete, interrupted, evaluated);

        var fromFlags = ServicePeriodLifecycle.StateOf(started, complete, interrupted, evaluated);

        fromFlags.Should().Be(ServicePeriodLifecycle.StateOf(period));
        ServicePeriodLifecycle.Predicate(fromFlags).Compile()(period).Should().BeTrue();
    }

    // ⚠ Publishing a répartition writes every period with IsStarted = false, and that is the state a
    // whole promotion's schedule sits in the day it is published. A screen that shows only started
    // periods shows nothing at all — which is how it was reported.
    [Fact]
    public void A_published_but_unopened_rotation_is_planned()
    {
        ServicePeriodLifecycle
            .StateOf(Period(started: false, complete: false, interrupted: false, evaluated: false))
            .Should().Be(ServicePeriodState.Planned);
    }

    // A pause suspends a rotation; it does not end it, and the student is still in the service.
    [Fact]
    public void A_paused_rotation_is_still_underway()
    {
        var period = Period(started: true, complete: false, interrupted: false, evaluated: false);
        period.IsPaused = true;

        ServicePeriodLifecycle.IsUnderway(period).Should().BeTrue();
        ServicePeriodLifecycle.StateOf(period).Should().Be(ServicePeriodState.Underway);
    }

    // An interrupted rotation is terminal whatever else is true of it: it was cut short by a
    // transfer, it can never be evaluated, and it must never appear as work anybody still owes.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void An_interrupted_rotation_is_settled_whatever_else_it_carries(bool started, bool complete)
    {
        ServicePeriodLifecycle
            .StateOf(Period(started, complete, interrupted: true, evaluated: false))
            .Should().Be(ServicePeriodState.Settled);
    }

    [Fact]
    public void A_closed_unmarked_rotation_is_what_a_chef_still_owes()
    {
        ServicePeriodLifecycle
            .StateOf(Period(started: true, complete: true, interrupted: false, evaluated: false))
            .Should().Be(ServicePeriodState.AwaitingEvaluation);
    }

    [Fact]
    public void A_closed_and_marked_rotation_is_settled()
    {
        ServicePeriodLifecycle
            .StateOf(Period(started: true, complete: true, interrupted: false, evaluated: true))
            .Should().Be(ServicePeriodState.Settled);
    }
}
