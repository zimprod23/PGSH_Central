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

    // ─── Movable: may this rotation's window still be moved? ──────────────────────

    /// <summary>
    /// Every combination of the four facts the move reads. ⚠ <b>Attendance is one of them</b>, and it
    /// is the one that has no bearing on any <see cref="ServicePeriodState"/> — which is exactly why
    /// « movable » is written as its own rule rather than derived from a state.
    /// </summary>
    public static TheoryData<bool, bool, bool, bool> AllMoveFactCombinations()
    {
        var data = new TheoryData<bool, bool, bool, bool>();
        foreach (bool started in new[] { false, true })
            foreach (bool complete in new[] { false, true })
                foreach (bool evaluated in new[] { false, true })
                    foreach (bool attended in new[] { false, true })
                        data.Add(started, complete, evaluated, attended);
        return data;
    }

    private static ServicePeriod MovablePeriod(bool started, bool complete, bool evaluated, bool attended)
    {
        var period = Period(started, complete, interrupted: false, evaluated);
        if (attended)
            period.Attendance.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                ServicePeriodId = period.Id,
                Date = new DateOnly(2026, 1, 13),
            });
        return period;
    }

    /// <summary>
    /// ⚠ <b>The two forms must not be able to disagree.</b> The expression is what EF compiles and the
    /// four-boolean overload is what a projection asks — the column-move guard reads the first, the
    /// pause report's « combien sont déplaçables » reads the second, and a screen that promises a move
    /// the aggregate then refuses is worse than a screen that promises nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllMoveFactCombinations))]
    public void The_entity_form_and_the_projection_form_agree_on_every_combination(
        bool started, bool complete, bool evaluated, bool attended)
    {
        ServicePeriodLifecycle
            .IsMovable(MovablePeriod(started, complete, evaluated, attended))
            .Should()
            .Be(ServicePeriodLifecycle.IsMovable(started, complete, evaluated, attended));
    }

    /// <summary>
    /// Exactly one combination moves: the rotation nothing has happened to. ⚠ Stated as its own test
    /// rather than left implicit in the agreement above, which would hold just as well if both forms
    /// were wrong in the same way.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllMoveFactCombinations))]
    public void Only_a_rotation_nothing_has_happened_to_may_move(
        bool started, bool complete, bool evaluated, bool attended)
    {
        bool untouched = !started && !complete && !evaluated && !attended;

        ServicePeriodLifecycle
            .IsMovable(MovablePeriod(started, complete, evaluated, attended))
            .Should().Be(untouched);
    }

    /// <summary>
    /// ⚠ A rotation that is <see cref="ServicePeriodState.Planned"/> and carries attendance is a row
    /// the lifecycle cannot produce and the store can hold — and it must <b>not</b> move: the days a
    /// secretary keyed in would end up on dates nobody served. This is the case that would be lost if
    /// « movable » were ever rewritten as « is it Planned ».
    /// </summary>
    [Fact]
    public void A_planned_rotation_carrying_attendance_is_planned_and_not_movable()
    {
        var period = MovablePeriod(started: false, complete: false, evaluated: false, attended: true);

        ServicePeriodLifecycle.StateOf(period).Should().Be(ServicePeriodState.Planned);
        ServicePeriodLifecycle.IsMovable(period).Should().BeFalse();
    }

    /// <summary>
    /// ⚠ Une rotation coupée par un transfert ne se déplace pas non plus. En pratique une
    /// interruption implique un démarrage, donc cette ligne est une ligne que le magasin peut porter
    /// et que le cycle de vie ne produit pas — et c'est précisément celle qu'un déplacement aurait
    /// réécrite en silence avant le 18/09/2026.
    /// </summary>
    [Fact]
    public void An_interrupted_rotation_never_moves_even_with_nothing_else_against_it()
    {
        var period = Period(started: false, complete: false, interrupted: true, evaluated: false);

        ServicePeriodLifecycle.IsMovable(period).Should().BeFalse();
    }

    // ─── Extendable: may this rotation's END be pushed back? ──────────────────────

    /// <summary>
    /// Les cinq faits dont dépendent les deux règles — 32 lignes, sans échantillonnage. L'interruption
    /// entre ici alors qu'elle était fixée à <c>false</c> pour <see cref="AllMoveFactCombinations"/> :
    /// c'est l'un des trois refus d'un allongement.
    /// </summary>
    public static TheoryData<bool, bool, bool, bool, bool> AllExtendFactCombinations()
    {
        var data = new TheoryData<bool, bool, bool, bool, bool>();
        foreach (bool started in new[] { false, true })
            foreach (bool complete in new[] { false, true })
                foreach (bool interrupted in new[] { false, true })
                    foreach (bool evaluated in new[] { false, true })
                        foreach (bool attended in new[] { false, true })
                            data.Add(started, complete, interrupted, evaluated, attended);
        return data;
    }

    private static ServicePeriod FullPeriod(
        bool started, bool complete, bool interrupted, bool evaluated, bool attended)
    {
        var period = Period(started, complete, interrupted, evaluated);
        if (attended)
            period.Attendance.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                ServicePeriodId = period.Id,
                Date = new DateOnly(2026, 1, 13),
            });
        return period;
    }

    /// <summary>
    /// ⚠ <b>Le théorème qui justifie qu'il y ait deux règles plutôt qu'une.</b> Tout ce qui se déplace
    /// s'allonge, jamais l'inverse — le même emboîtement que <c>CountsTowardDuration</c> et
    /// <c>CanBoundAWindow</c>, et pour la même raison : deux prédicats indépendants finiraient par
    /// répondre des choses incompatibles sur une même ligne, et un acte de rattrapage choisirait le
    /// mauvais.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllExtendFactCombinations))]
    public void Everything_movable_is_extendable_and_not_the_other_way_round(
        bool started, bool complete, bool interrupted, bool evaluated, bool attended)
    {
        var period = FullPeriod(started, complete, interrupted, evaluated, attended);

        if (ServicePeriodLifecycle.IsMovable(period))
            ServicePeriodLifecycle.IsExtendable(period).Should().BeTrue(
                "a window that may move entirely may certainly have its end pushed");
    }

    /// <summary>
    /// ⚠ <b>Le cas pour lequel la règle existe.</b> Une rotation commencée — ou commencée et pointée —
    /// ne se déplace pas et s'allonge : c'est exactement l'état d'une promotion surprise par une
    /// fermeture en cours d'année, et sous la seule règle <see cref="ServicePeriodLifecycle.Movable"/>
    /// elle était irrattrapable.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_started_rotation_cannot_move_but_can_be_extended(bool attended)
    {
        var period = FullPeriod(
            started: true, complete: false, interrupted: false, evaluated: false, attended);

        ServicePeriodLifecycle.IsMovable(period).Should().BeFalse();
        ServicePeriodLifecycle.IsExtendable(period).Should().BeTrue();
    }

    /// <summary>
    /// Les trois refus, nommés un par un plutôt que laissés à l'accord des deux formes — lequel
    /// tiendrait tout aussi bien si les deux étaient fausses de la même manière.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllExtendFactCombinations))]
    public void Only_a_closure_a_mark_or_an_interruption_refuses_an_extension(
        bool started, bool complete, bool interrupted, bool evaluated, bool attended)
    {
        bool open = !complete && !interrupted && !evaluated;

        ServicePeriodLifecycle
            .IsExtendable(FullPeriod(started, complete, interrupted, evaluated, attended))
            .Should().Be(open);
    }

    /// <summary>
    /// ⚠ L'expression et la projection plate ne doivent pas pouvoir diverger — l'aperçu du recalcul
    /// compte « combien de colonnes s'allongent » depuis une projection, et l'agrégat refuse depuis
    /// l'entité.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllExtendFactCombinations))]
    public void The_entity_form_and_the_projection_form_agree_on_extension(
        bool started, bool complete, bool interrupted, bool evaluated, bool attended)
    {
        ServicePeriodLifecycle
            .IsExtendable(FullPeriod(started, complete, interrupted, evaluated, attended))
            .Should()
            .Be(ServicePeriodLifecycle.IsExtendable(complete, interrupted, evaluated));
    }

    /// <summary>
    /// ⚠ <b>Les présences n'entrent pas dans l'allongement, et c'est un choix mesuré.</b> Une journée
    /// pointée vit entre le début et l'ancienne fin ; une fenêtre qui ne fait que croître la contient
    /// toujours. La garde qui manquerait — ramener la fin en arrière — vit dans le nom de l'acte
    /// (<c>ExtendTo</c>), pas ici.
    /// </summary>
    // ─── MovableOn: the dated question ───────────────────────────────────────────

    private static readonly DateOnly Today = new(2026, 9, 19);

    private static ServicePeriod Windowed(
        DateOnly start, bool started, bool complete = false,
        bool interrupted = false, bool evaluated = false, bool attended = false)
    {
        var period = FullPeriod(started, complete, interrupted, evaluated, attended);
        period.StartDate = start;
        period.EndDate = start.AddDays(30);
        return period;
    }

    /// <summary>
    /// ⚠ <b>Le cas mesuré sur la base vivante le 19/09/2026 : 305 périodes de la 4ᵉ MED.</b>
    /// <c>Start()</c> est un <i>whole-student start</i>, donc un séjour de février porte
    /// <c>IsStarted</c> dès septembre. Un recalcul lisant <see cref="ServicePeriodLifecycle.Movable"/>
    /// refuserait de pousser précisément les rotations futures qu'il existe pour pousser.
    /// </summary>
    [Fact]
    public void A_future_rotation_flagged_started_by_a_whole_student_start_may_still_move()
    {
        var february = Windowed(new DateOnly(2027, 2, 1), started: true);

        ServicePeriodLifecycle.IsMovable(february).Should().BeFalse("the flag alone refuses it");
        ServicePeriodLifecycle.IsMovableOn(february, Today).Should().BeTrue(
            "nothing has happened in it and it does not begin for months");
    }

    /// <summary>
    /// ⚠ L'autre sens, et c'est pourquoi les deux règles ne sont pas emboîtées : une rotation jamais
    /// ouverte dont la fenêtre est <i>passée</i> est `Movable` et n'est pas `MovableOn`. Plus strict,
    /// et à raison — ses dates ont eu lieu, ouverte ou non.
    /// </summary>
    [Fact]
    public void A_past_rotation_nobody_opened_is_Movable_and_not_movable_today()
    {
        var june = Windowed(new DateOnly(2026, 6, 1), started: false);

        ServicePeriodLifecycle.IsMovable(june).Should().BeTrue();
        ServicePeriodLifecycle.IsMovableOn(june, Today).Should().BeFalse();
    }

    /// <summary>
    /// ⚠ Les faits enregistrés gardent leur veto quelle que soit la date : le magasin peut porter une
    /// présence sur une rotation à venir, et la déplacer laisserait cette journée sur une date que
    /// personne n'a servie.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_recorded_fact_refuses_the_move_however_far_away_the_window_is(
        bool evaluated, bool attended)
    {
        var future = Windowed(
            new DateOnly(2027, 2, 1), started: false, evaluated: evaluated, attended: attended);

        ServicePeriodLifecycle.IsMovableOn(future, Today).Should().BeFalse();
    }

    /// <summary>The nesting that does hold: everything movable today may be extended.</summary>
    [Theory]
    [MemberData(nameof(AllExtendFactCombinations))]
    public void Everything_movable_today_is_extendable(
        bool started, bool complete, bool interrupted, bool evaluated, bool attended)
    {
        var period = Windowed(new DateOnly(2027, 2, 1), started, complete, interrupted, evaluated, attended);

        if (ServicePeriodLifecycle.IsMovableOn(period, Today))
            ServicePeriodLifecycle.IsExtendable(period).Should().BeTrue();
    }

    /// <summary>⚠ The entity form and the projection form must not diverge here either.</summary>
    [Theory]
    [MemberData(nameof(AllExtendFactCombinations))]
    public void The_entity_form_and_the_projection_form_agree_on_movable_today(
        bool started, bool complete, bool interrupted, bool evaluated, bool attended)
    {
        var start = new DateOnly(2027, 2, 1);
        var period = Windowed(start, started, complete, interrupted, evaluated, attended);

        ServicePeriodLifecycle.IsMovableOn(period, Today)
            .Should()
            .Be(ServicePeriodLifecycle.IsMovableOn(
                complete, interrupted, evaluated, attended, start, Today));
    }

    [Fact]
    public void Attendance_blocks_a_move_and_does_not_block_an_extension()
    {
        var period = FullPeriod(
            started: true, complete: false, interrupted: false, evaluated: false, attended: true);

        ServicePeriodLifecycle.IsMovable(period).Should().BeFalse();
        ServicePeriodLifecycle.IsExtendable(period).Should().BeTrue();
    }
}
