namespace PGSH.Domain.Registrations;

/// <summary>
/// Why a registration is held back from planning until somebody looks at it.
/// </summary>
/// <remarks>
/// <para>Each value is a fact PGSH established at the moment the hold was raised, never a live
/// re-evaluated condition — the same rule <c>CnpnTargeting</c> follows and for the same reason. The
/// debt that raised <see cref="OutstandingPriorStages"/> may be cleared the following week; what
/// releases the hold is somebody saying so, not the condition quietly ceasing to hold. Otherwise a
/// registration would slip back into a répartition with nobody having decided that it should.</para>
/// </remarks>
public enum RegistrationHoldReason
{
    /// <summary>
    /// Registered into the last year of his own text while stages from earlier years are still
    /// unvalidated.
    ///
    /// <para>⚠ <b>This is not a refusal wearing another name.</b> The faculty's réinscription roll
    /// names him as coming back, and it outranks our reading of a stage record that is mostly not
    /// entered yet: measured on the 2026-2027 roll, 182 of the 651 7ᵉ année Médecine it re-registers
    /// read as owing something, and in most of those cases the stage was served and the évaluation
    /// simply has not been keyed in. So the registration is created — refusing it refuses him the
    /// only mechanism that clears the debt — and held, because he may not start his final year's
    /// stages before the earlier ones are settled.</para>
    /// </summary>
    OutstandingPriorStages,

    /// <summary>
    /// He holds a registration in the closing year and the faculty's roll does not name him.
    ///
    /// <para>The roll is the list of who <em>is</em> coming back, so an absence says he is not — but
    /// it does not say why, and the three causes call for opposite acts: a defence, an abandon or an
    /// exclusion, or a réinscription that simply has not arrived. Where it is his last year the
    /// absence is recorded « Diplômé » <c>Inferred</c>; everywhere else nothing is written at all.
    /// Either way the hold is what stops the inference — or the silence — from being acted on before
    /// a human has confirmed it.</para>
    /// </summary>
    AbsentFromReinscriptionRoll,

    /// <summary>
    /// He exists because a file named him, and PGSH holds almost nothing else about him.
    ///
    /// <para>The faculty's réinscription roll carries a code and a name and nothing more. 26 of its
    /// lines named people PGSH had never seen; they used to be skipped, which left them in a
    /// spreadsheet and nowhere in the application. They are created instead, and marked with this so
    /// somebody completes the file — CNE, e-mail réel, date de naissance, and whatever the
    /// équivalence requires for a transfer.</para>
    ///
    /// <para>⚠ <b>This one does not freeze, and that is the whole point of it being separate.</b> His
    /// dossier is <em>thin</em>, not <em>wrong</em>: nothing about a missing birth date says he may
    /// not rotate through a service. He is cut into a roster and planned like anyone else, and the
    /// flag is there so his file gets finished — not to hold up a promotion until it is. See
    /// <see cref="RegistrationHoldReasonExtensions.Blocking"/>.</para>
    /// </summary>
    IncompleteStudentFile,
}

public static class RegistrationHoldReasonExtensions
{
    /// <summary>
    /// The reasons that withdraw a registration from planning. Everything not named here is
    /// <b>advisory</b>: it appears on the worklist and takes part in the répartition exactly like an
    /// unflagged registration.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Whether a signalement freezes is a property of the reason, not of the flag.</b> The
    /// first two reasons say « nobody has established that this student may go on » — a debt not
    /// cleared, an absence not explained — and acting on either before a human rules would send a
    /// student somewhere he may not belong. <c>IncompleteStudentFile</c> says something different and
    /// much weaker: we know who he is and we are missing his paperwork. Collapsing the two would
    /// either freeze people over a missing birth date or let an unexplained absence plan itself.</para>
    ///
    /// <para>⚠ <b>A list, not a method, because <see cref="RegistrationHoldPolicy"/> has to translate
    /// it.</b> EF cannot call <see cref="BlocksPlanning"/> inside a predicate; it can translate
    /// <c>Contains</c> over a static array into an <c>IN</c>. So the array is the single statement of
    /// the rule and the method reads it, rather than the two being written out separately and drifting.
    /// </para>
    /// </remarks>
    public static readonly RegistrationHoldReason[] Blocking =
    [
        RegistrationHoldReason.OutstandingPriorStages,
        RegistrationHoldReason.AbsentFromReinscriptionRoll,
    ];

    /// <summary>Whether this reason withdraws the registration from planning.</summary>
    public static bool BlocksPlanning(this RegistrationHoldReason reason) =>
        Blocking.Contains(reason);

    /// <summary>
    /// The French wording, stated once. Every screen, every refusal and the export print the same
    /// phrase for the same reason — the same rule <c>ServicePeriodLifecycle</c> follows, and for the
    /// same reason: a worklist and the message explaining why a student is on it must not describe
    /// the flag differently.
    /// </summary>
    public static string Label(this RegistrationHoldReason reason) => reason switch
    {
        RegistrationHoldReason.OutstandingPriorStages => "stages antérieurs non validés",
        RegistrationHoldReason.AbsentFromReinscriptionRoll => "absent du fichier de réinscription",
        RegistrationHoldReason.IncompleteStudentFile => "dossier à compléter",
        _ => reason.ToString(),
    };

    /// <summary>What has to happen for the hold to be releasable, in the operator's own terms.</summary>
    public static string Remedy(this RegistrationHoldReason reason) => reason switch
    {
        RegistrationHoldReason.OutstandingPriorStages =>
            "Saisir les évaluations manquantes ou faire revalider les stages dus, puis lever le signalement.",
        RegistrationHoldReason.AbsentFromReinscriptionRoll =>
            "Confirmer la soutenance, l'abandon ou l'exclusion — ou inscrire l'étudiant s'il revient — "
            + "puis lever le signalement.",

        RegistrationHoldReason.IncompleteStudentFile =>
            "Compléter sa fiche — CNE, adresse e-mail réelle, date et lieu de naissance, et "
            + "l'équivalence s'il vient d'un autre établissement — puis lever le signalement. "
            + "Il participe déjà à la planification : rien n'est bloqué en attendant.",
        _ => "Vérifier la situation de l'étudiant, puis lever le signalement.",
    };
}
