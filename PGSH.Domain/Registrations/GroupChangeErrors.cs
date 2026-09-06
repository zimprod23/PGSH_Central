using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// The refusals of « changement de groupe » — the act that moves a student between two rosters
/// <b>without leaving a trace</b>, so that the record reads as though he had been in the target roster
/// from the start.
/// </summary>
/// <remarks>
/// <para>⚠ <b>It is a correction, not a movement, and every refusal here defends that difference.</b>
/// A <c>TransferStudentCommand</c> says « il était là, il est maintenant ici » and writes that down —
/// a <c>HistoryType.GroupTransfer</c> row, a closed <c>CohortMembership</c> and an open one, an
/// interrupted période where the rotation was cut. This act says « il a toujours été ici », which is
/// only true while nothing on record contradicts it. Once a rotation has begun the two are not
/// interchangeable and the refusals name the transfer instead of offering a force — the same rule that
/// keeps <c>AcademicGroupErrors.RosterAffectationsUnderway</c> unforceable.</para>
/// </remarks>
public static class GroupChangeErrors
{
    /// <summary>
    /// Nothing to correct: the registration is in no roster at all. Joining one is a different act with
    /// different consequences — it materialises the rotations the roster still has ahead of it — and
    /// running it as a correction would give the student a roster pointer and no cohorte.
    /// </summary>
    public static Error NotInAGroup() => Error.Conflict(
        "GroupChange.NotInAGroup",
        "Cet étudiant n'est dans aucun groupe : il n'y a pas de groupe à corriger. Utilisez "
        + "« Affecter à un groupe », qui lui crée aussi les cohortes et les périodes du groupe.");

    /// <summary>Already there. Not an error worth a stack trace, but the act would write nothing.</summary>
    public static Error AlreadyInTargetGroup(string groupLabel) => Error.Conflict(
        "GroupChange.AlreadyInTargetGroup",
        $"Cet étudiant est déjà dans « {groupLabel} ».");

    /// <summary>
    /// « Non réparti » — the bucket that belongs to no promotion and carries no cohorte.
    /// </summary>
    /// <remarks>
    /// ⚠ Landing a student there is not a change of group, it is an un-assignment: he would keep his
    /// affectations in the cohortes of the roster he came from — on the chefs' worklists, counted in the
    /// services' effectifs — while his file says he is nowhere. That is precisely the state
    /// « Vider le groupe » exists to refuse, and it names what it costs.
    /// </remarks>
    public static Error TargetIsUnassignedRoster(string groupLabel) => Error.Conflict(
        "GroupChange.TargetIsUnassignedRoster",
        $"« {groupLabel} » ne rassemble que les inscriptions non réparties : il ne porte aucune "
        + "cohorte, donc y « changer » un étudiant le laisserait affecté aux cohortes de son ancien "
        + "groupe. Pour le retirer de la répartition, utilisez « Vider le groupe », qui indique ce que "
        + "cela emporte.");

    /// <summary>
    /// Something has actually happened to this student's rotations. The counts are the four the
    /// unpublish path names, read through the same <c>AffectationToll</c>, so two acts cannot describe
    /// the same rows differently.
    /// </summary>
    public static Error RotationsUnderway(
        string groupLabel, int periods, int started, int evaluated, int attendanceDays) => Error.Conflict(
        "GroupChange.RotationsUnderway",
        $"Les rotations de cet étudiant dans « {groupLabel} » sont engagées : sur {periods} période(s), "
        + $"{started} ont démarré, {evaluated} portent une évaluation et {attendanceDays} journée(s) de "
        + "présence sont enregistrées. Un changement de groupe déclare qu'il n'y a jamais été, ce que "
        + "ces enregistrements contredisent. Utilisez un transfert : il fait suivre la rotation et en "
        + "garde la trace.");

    /// <summary>
    /// The target roster runs a different set of stages, so one of the student's affectations has
    /// nowhere to land.
    /// </summary>
    /// <remarks>
    /// ⚠ Refused rather than silently dropped: the affectation is what records that the student owes
    /// the stage, and deleting it would take the obligation with it. Rare in practice — measured
    /// 2026-09-06, every roster of a promotion carries exactly the same cohortes (7/7 en 5ᵉ MED, 6/6 en
    /// 3ᵉ MED, 5/5 en 4ᵉ MED, 2/2 en 5ᵉ Pharmacie) — but a roster of another CNPN legitimately differs,
    /// since <c>CohortProvisioner</c> skips the stages a text does not require.
    /// </remarks>
    public static Error TargetRosterMissingStage(string groupLabel, string stageName) => Error.Conflict(
        "GroupChange.TargetRosterMissingStage",
        $"« {groupLabel} » ne porte aucune cohorte pour « {stageName} », que cet étudiant doit. "
        + "L'y déplacer supprimerait l'affectation qui enregistre qu'il le doit. Provisionnez d'abord "
        + "les cohortes de ce stage pour le groupe d'arrivée.");

    /// <summary>
    /// The student already holds an affectation in the target cohorte — a revalidation placed there by
    /// hand, or a half-finished earlier move.
    /// </summary>
    /// <remarks>
    /// ⚠ Moving the second one in would leave <b>two</b> affectations on one (inscription, cohorte):
    /// double in the dossier, double against the service's quota, two rows for one rotation. That is
    /// the exact duplication « Vider le groupe » then a re-découpage used to produce.
    /// </remarks>
    public static Error AlreadyAffectedInTargetCohort(string groupLabel, string stageName) => Error.Conflict(
        "GroupChange.AlreadyAffectedInTargetCohort",
        $"Cet étudiant tient déjà une affectation pour « {stageName} » dans « {groupLabel} ». "
        + "En déplacer une seconde lui en donnerait deux pour le même stage — comptées deux fois dans "
        + "son dossier et dans les effectifs du service. Vérifiez ses affectations pour ce stage.");

    /// <summary>An échange needs two students.</summary>
    public static Error CannotSwapWithSelf() => Error.Validation(
        "GroupChange.CannotSwapWithSelf",
        "Un échange demande deux étudiants différents.");

    /// <summary>
    /// Both already in the same roster: there is nothing to exchange, and running it would move each of
    /// them into the group he is already in.
    /// </summary>
    public static Error SwapWithinOneGroup(string groupLabel) => Error.Conflict(
        "GroupChange.SwapWithinOneGroup",
        $"Ces deux étudiants sont tous les deux dans « {groupLabel} » : il n'y a rien à échanger.");
}
