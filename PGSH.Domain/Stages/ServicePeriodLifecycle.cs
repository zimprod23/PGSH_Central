using System.Linq.Expressions;

namespace PGSH.Domain.Stages;

/// <summary>
/// Where one rotation stands. Four states, and they <b>partition</b> a
/// <see cref="ServicePeriod"/>: every period is in exactly one, whatever combination of flags it
/// carries — including the nonsensical ones a store can hold but the lifecycle cannot produce.
/// </summary>
public enum ServicePeriodState
{
    /// <summary>
    /// Published by the administration and not opened. ⚠ This is the state a répartition lands in:
    /// publishing writes every period with <c>IsStarted = false</c>, and starting is a separate
    /// administrative act. A screen that only shows started periods therefore shows nothing at all
    /// the day a whole promotion is published, which is exactly how it was reported.
    /// </summary>
    Planned,

    /// <summary>Open: the student is standing in the service. Includes a paused rotation.</summary>
    Underway,

    /// <summary>Closed by the administration and unmarked — the chef's actual worklist.</summary>
    AwaitingEvaluation,

    /// <summary>
    /// Nothing further is owed: marked, or cut short by a mid-stage transfer
    /// (<see cref="ServicePeriod.IsInterrupted"/>) and therefore terminal.
    /// </summary>
    Settled,
}

/// <summary>
/// Single source of truth for which <see cref="ServicePeriodState"/> a rotation is in — shared by
/// the query side (the chef worklist and its counts), the planning services that act on open or
/// not-yet-open rotations, and the read models that report the state to a client.
///
/// <para>Same reason <see cref="StageScoring"/> exists: the rule was being restated as a raw boolean
/// triple wherever it was needed. <c>IsStarted &amp;&amp; !IsComplete &amp;&amp; !IsInterrupted</c>
/// was written out in four different files, and <c>!IsStarted &amp;&amp; !IsComplete &amp;&amp;
/// !IsInterrupted</c> in two — six chances for one of them to disagree about what "en cours" means,
/// with nothing to catch it.</para>
///
/// <para>⚠ <b>The expressions are the authority; the delegates are compiled from them.</b> Two hand
/// written copies — one for EF, one for memory — is the very drift this class removes. EF needs an
/// <see cref="Expression"/> (a method call in a <c>Where</c> is refused by the provider, see
/// <c>SqlTranslationTests</c>), so the expression is what is written and
/// <see cref="Expression{TDelegate}.Compile"/> produces the in-memory form.</para>
///
/// <para>⚠ <see cref="AwaitingEvaluation"/> and <see cref="Settled"/> read
/// <see cref="ServicePeriod.Evaluation"/>. Against the store that is a join; in memory the
/// navigation must actually be loaded, or an unmarked period reads as awaiting when it is settled.
/// <see cref="Planned"/> and <see cref="Underway"/> touch flags only and are always safe.</para>
/// </summary>
public static class ServicePeriodLifecycle
{
    /// <summary>Published, not opened, nothing recorded against it.</summary>
    public static readonly Expression<Func<ServicePeriod, bool>> Planned =
        p => !p.IsInterrupted && !p.IsStarted && !p.IsComplete;

    /// <summary>Open — the student is there. A paused rotation is still underway.</summary>
    public static readonly Expression<Func<ServicePeriod, bool>> Underway =
        p => !p.IsInterrupted && p.IsStarted && !p.IsComplete;

    /// <summary>Closed and unmarked.</summary>
    public static readonly Expression<Func<ServicePeriod, bool>> AwaitingEvaluation =
        p => !p.IsInterrupted && p.IsStarted && p.IsComplete && p.Evaluation == null;

    /// <summary>
    /// Everything else, written as the exact complement of the other three rather than as
    /// "closed and marked". ⚠ That difference is what makes the four a partition: a row that is
    /// complete but never started is a state the lifecycle cannot produce and a store can still
    /// hold, and under a positive definition it would belong to no state at all — so it would vanish
    /// from every list while still being counted in none of them.
    /// </summary>
    public static readonly Expression<Func<ServicePeriod, bool>> Settled =
        p => p.IsInterrupted || (p.IsComplete && (!p.IsStarted || p.Evaluation != null));

    private static readonly Func<ServicePeriod, bool> PlannedFn = Planned.Compile();
    private static readonly Func<ServicePeriod, bool> UnderwayFn = Underway.Compile();
    private static readonly Func<ServicePeriod, bool> AwaitingFn = AwaitingEvaluation.Compile();

    /// <summary>The store-side predicate for <paramref name="state"/> — pass it straight to a <c>Where</c>.</summary>
    public static Expression<Func<ServicePeriod, bool>> Predicate(ServicePeriodState state) => state switch
    {
        ServicePeriodState.Planned            => Planned,
        ServicePeriodState.Underway           => Underway,
        ServicePeriodState.AwaitingEvaluation => AwaitingEvaluation,
        _                                     => Settled,
    };

    /// <summary>Is this loaded period open? Flags only, so the evaluation need not be loaded.</summary>
    public static bool IsUnderway(ServicePeriod period) => UnderwayFn(period);

    /// <summary>Is this loaded period published and not yet opened? Flags only.</summary>
    public static bool IsPlanned(ServicePeriod period) => PlannedFn(period);

    /// <summary>
    /// The state of a loaded period. ⚠ Requires <see cref="ServicePeriod.Evaluation"/> to be loaded;
    /// use <see cref="StateOf(bool, bool, bool, bool)"/> when working from a projection.
    /// </summary>
    public static ServicePeriodState StateOf(ServicePeriod period) =>
        PlannedFn(period) ? ServicePeriodState.Planned
        : UnderwayFn(period) ? ServicePeriodState.Underway
        : AwaitingFn(period) ? ServicePeriodState.AwaitingEvaluation
        : ServicePeriodState.Settled;

    /// <summary>
    /// The same decision from a flat projection, for a read model that has already selected the four
    /// facts and must not drag the whole entity back to re-ask. Kept beside the expressions it
    /// mirrors, and pinned against them by <c>ServicePeriodLifecycleTests</c>.
    /// </summary>
    public static ServicePeriodState StateOf(bool isStarted, bool isComplete, bool isInterrupted, bool hasEvaluation) =>
        isInterrupted ? ServicePeriodState.Settled
        : !isStarted && !isComplete ? ServicePeriodState.Planned
        : isStarted && !isComplete ? ServicePeriodState.Underway
        : isStarted && isComplete && !hasEvaluation ? ServicePeriodState.AwaitingEvaluation
        : ServicePeriodState.Settled;
}
