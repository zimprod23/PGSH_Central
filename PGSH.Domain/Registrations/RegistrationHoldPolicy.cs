using System.Linq.Expressions;

namespace PGSH.Domain.Registrations;

/// <summary>
/// Single source of truth for « cette inscription participe-t-elle à la planification ? ».
///
/// <para>Same reason <see cref="Stages.ServicePeriodLifecycle"/> and <see cref="Stages.StageScoring"/>
/// exist: the rule is needed by the roster cut, by the cohort provisioning, by the affectation
/// service and by the screens that report what each of them left out, and a rule restated in five
/// places is five chances to disagree about who is frozen with nothing able to catch it.</para>
///
/// <para>⚠ <b>The expressions are the authority; the delegates are compiled from them.</b> EF needs
/// an <see cref="Expression"/> — a method call in a <c>Where</c> is refused by the provider — so the
/// expression is what is written, and <see cref="Expression{TDelegate}.Compile"/> gives the
/// in-memory form. Two hand-written copies is the drift this class removes.</para>
///
/// <para>⚠ <b>Both forms read the <see cref="Registration.Holds"/> collection.</b> Against the store
/// that is an <c>EXISTS</c>, which translates; in memory the navigation must actually be loaded, or
/// a held registration reads as free. Prefer the expression in every query — a predicate is the one
/// place a collection may be aggregated without meeting the shape Npgsql refuses, which is a
/// collection subquery in a <em>projection</em>.</para>
/// </summary>
public static class RegistrationHoldPolicy
{
    /// <summary>
    /// Carries no unreleased <b>blocking</b> hold, so planning may reach it.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Not « carries no hold ».</b> A signalement means « a human must look at this »; whether it
    /// also withdraws the registration from planning is decided by the reason
    /// (<see cref="RegistrationHoldReasonExtensions.Blocking"/>). A student created from the
    /// réinscription roll with nothing but a code and a name is flagged so his file gets completed —
    /// and is cut into a roster and planned meanwhile, because a missing date de naissance is no
    /// reason to keep him out of a rotation.
    /// </remarks>
    public static readonly Expression<Func<Registration, bool>> Plannable =
        r => !r.Holds.Any(h => h.ReleasedOn == null
                            && RegistrationHoldReasonExtensions.Blocking.Contains(h.Reason));

    /// <summary>Carries at least one unreleased blocking hold, so planning must leave it alone.</summary>
    public static readonly Expression<Func<Registration, bool>> OnHold =
        r => r.Holds.Any(h => h.ReleasedOn == null
                           && RegistrationHoldReasonExtensions.Blocking.Contains(h.Reason));

    /// <summary>
    /// Carries any unreleased hold at all, blocking or advisory — « quelqu'un doit regarder ceci ».
    /// This is what the worklist counts; <see cref="OnHold"/> is what planning obeys.
    /// </summary>
    public static readonly Expression<Func<Registration, bool>> Flagged =
        r => r.Holds.Any(h => h.ReleasedOn == null);

    private static readonly Func<Registration, bool> PlannableFunc = Plannable.Compile();
    private static readonly Func<Registration, bool> FlaggedFunc = Flagged.Compile();

    /// <summary>The in-memory form of <see cref="Plannable"/>. Requires <c>Holds</c> to be loaded.</summary>
    public static bool IsPlannable(Registration registration) => PlannableFunc(registration);

    /// <summary>The in-memory form of <see cref="OnHold"/>. Requires <c>Holds</c> to be loaded.</summary>
    public static bool IsOnHold(Registration registration) => !PlannableFunc(registration);

    /// <summary>The in-memory form of <see cref="Flagged"/>. Requires <c>Holds</c> to be loaded.</summary>
    public static bool IsFlagged(Registration registration) => FlaggedFunc(registration);
}
