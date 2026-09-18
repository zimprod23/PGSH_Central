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
/// <para>⚠ <b>Deux règles de cette classe ne sont pas des états, et ne s'en déduisent pas :</b>
/// <see cref="Movable"/> (« puis-je déplacer le début ? ») et <see cref="Extendable"/> (« puis-je
/// repousser la fin ? »). Elles lisent des faits — une présence, une note — dont aucun état ne
/// dépend, et elles sont emboîtées dans cet ordre.</para>
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

    /// <summary>
    /// Nothing has been recorded against this rotation, so its window may still be <b>moved</b> —
    /// the invariant <c>InternshipAssignment.Reschedule</c> enforces, and the question a column move
    /// and the promotion-pause report both have to ask before they can say anything useful.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Not the complement of a state, and deliberately not written as one.</b> "May this
    /// move?" is a different question from "where does this stand?": a <see cref="Planned"/> rotation
    /// carrying attendance days is a row the lifecycle cannot produce and the store can hold, and
    /// moving it would leave those days on dates nobody served. So the four facts are read directly
    /// rather than inferred from a state.</para>
    ///
    /// <para>⚠ <b>Why it lives here rather than in the aggregate that enforces it.</b> The rule was
    /// written out in <c>InternshipAssignment.Reschedule</c> and again in
    /// <c>PublishedPeriodShifter.PlanAsync</c>, and the pause report needed it a third time — the
    /// same drift that produced this class in the first place. A guard that refuses and a report that
    /// promises must read one rule, or the screen offers a move the aggregate then refuses.</para>
    ///
    /// <para>⚠ <b>Reads <see cref="ServicePeriod.Evaluation"/> <i>and</i>
    /// <see cref="ServicePeriod.Attendance"/>.</b> Against the store both are joins; in memory both
    /// must actually be loaded, or an un-Included collection makes a rotation carrying a mark and a
    /// month of présences read as untouched. That is what
    /// <see cref="IsMovable(bool, bool, bool, bool)"/> is for.</para>
    ///
    /// <para>⚠ <b><see cref="ServicePeriod.IsInterrupted"/> refuse ici aussi, ajouté le 18/09/2026
    /// avec <see cref="Extendable"/>.</b> Une rotation coupée par un transfert a pour fenêtre ce qui
    /// a réellement été servi avant la coupure : ses deux bouts sont des faits. En pratique une
    /// interruption implique un démarrage, donc rien ne change pour les lignes que le cycle de vie
    /// sait produire ; ce que cela ferme est une ligne que le magasin peut porter et qu'un
    /// déplacement aurait réécrite en silence. C'est aussi ce qui fait de l'emboîtement
    /// <see cref="Movable"/> ⊂ <see cref="Extendable"/> un théorème plutôt qu'une coïncidence.</para>
    /// </remarks>
    public static readonly Expression<Func<ServicePeriod, bool>> Movable =
        p => !p.IsInterrupted && !p.IsStarted && !p.IsComplete
          && p.Evaluation == null && !p.Attendance.Any();

    /// <summary>
    /// Peut-on repousser la <b>fin</b> de cette rotation, en laissant son début où il est ?
    /// L'autre moitié de la question que <see cref="Movable"/> posait seule.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Deux questions, pas une — et les confondre est ce qui rendait irréparable le cas
    /// qui compte.</b> « Puis-je déplacer le <i>début</i> ? » est non dès qu'une rotation a commencé :
    /// quelque chose a eu lieu à cette date. « Puis-je repousser la <i>fin</i> ? » est oui, parce
    /// qu'allonger un séjour ne réécrit rien de ce qui s'est passé. Une fenêtre d'examens déclarée en
    /// cours d'année tombe justement sur des rotations commencées, et <see cref="Movable"/> seul y
    /// répondait « rien n'est rattrapable ».</para>
    ///
    /// <para>⚠ <b>Les présences n'entrent pas ici, et c'est le cœur de la distinction.</b> Elles
    /// interdisent un <i>déplacement</i> — les journées pointées se retrouveraient sur des dates que
    /// personne n'a servies — et n'interdisent pas un <i>allongement</i> : toute journée comprise dans
    /// l'ancienne fenêtre l'est encore dans une fenêtre qui ne fait que s'étendre. C'est la raison
    /// pour laquelle la garde du raccourcissement n'est pas ici mais dans le nom de l'acte :
    /// <c>InternshipAssignment.ExtendTo</c> ne sait que repousser.</para>
    ///
    /// <para>⚠ <b>Ce qui refuse.</b> Une rotation <see cref="ServicePeriod.IsComplete"/> est close et
    /// sa fin est un fait ; une rotation notée a été jugée sur ce séjour-là ; une rotation
    /// <see cref="ServicePeriod.IsInterrupted"/> a été coupée par un transfert et sa fin <b>est</b> la
    /// date du transfert. Aucune des trois ne s'allonge sans faire mentir le registre.</para>
    ///
    /// <para>⚠ <b>Les deux règles sont emboîtées, comme <c>CountsTowardDuration</c> et
    /// <c>CanBoundAWindow</c> l'ont été pour les mêmes raisons : tout ce qui se déplace s'allonge, pas
    /// l'inverse.</b> C'est ce qui rend <see cref="Movable"/> utilisable comme cas particulier sans
    /// que les deux puissent diverger, et <c>ServicePeriodLifecycleTests</c> le vérifie sur les
    /// trente-deux combinaisons plutôt que sur un échantillon.</para>
    /// </remarks>
    public static readonly Expression<Func<ServicePeriod, bool>> Extendable =
        p => !p.IsInterrupted && !p.IsComplete && p.Evaluation == null;

    private static readonly Func<ServicePeriod, bool> PlannedFn = Planned.Compile();
    private static readonly Func<ServicePeriod, bool> UnderwayFn = Underway.Compile();
    private static readonly Func<ServicePeriod, bool> AwaitingFn = AwaitingEvaluation.Compile();
    private static readonly Func<ServicePeriod, bool> MovableFn = Movable.Compile();
    private static readonly Func<ServicePeriod, bool> ExtendableFn = Extendable.Compile();

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
    /// May this loaded rotation's window still be moved? ⚠ Unlike <see cref="IsUnderway"/> this reads
    /// two navigations: <see cref="ServicePeriod.Evaluation"/> and
    /// <see cref="ServicePeriod.Attendance"/> must both be loaded, or the answer is "yes" for a
    /// rotation that carries both.
    /// </summary>
    public static bool IsMovable(ServicePeriod period) => MovableFn(period);

    /// <summary>
    /// The same decision from a flat projection, for a caller that has already selected the four
    /// facts — and for the store-side reads where dragging the whole entity back is the wrong shape.
    /// Kept beside the expression it mirrors and pinned against it by
    /// <c>ServicePeriodLifecycleTests</c>.
    /// </summary>
    public static bool IsMovable(bool isStarted, bool isComplete, bool hasEvaluation, bool hasAttendance) =>
        !isStarted && !isComplete && !hasEvaluation && !hasAttendance;

    /// <summary>
    /// Peut-on repousser la fin de cette rotation chargée ? ⚠ Lit
    /// <see cref="ServicePeriod.Evaluation"/>, qui doit être chargée — mais <b>pas</b>
    /// <see cref="ServicePeriod.Attendance"/>, qu'un allongement ne peut pas orpheliner.
    /// </summary>
    public static bool IsExtendable(ServicePeriod period) => ExtendableFn(period);

    /// <summary>
    /// La même décision depuis une projection plate. ⚠ Trois faits et non quatre : le démarrage
    /// n'entre pas — c'est tout le propos — et les présences non plus.
    /// </summary>
    public static bool IsExtendable(bool isComplete, bool isInterrupted, bool hasEvaluation) =>
        !isInterrupted && !isComplete && !hasEvaluation;

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
