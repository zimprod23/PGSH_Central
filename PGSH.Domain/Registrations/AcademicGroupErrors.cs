using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// The refusals that keep a roster inside its promotion.
///
/// <para>⚠ <b>A roster is identified by (année, promotion, numéro)</b> — <c>IX_AcademicGroup_Year_Level_Number</c>.
/// The index makes two rosters distinguishable; it cannot stop a <i>student</i> from being moved into a
/// roster of another promotion, or a <i>cohorte</i> from being built on one, because both are ordinary
/// FKs to a row that exists. Those are the two write paths this class refuses, and they are the last
/// ones through which the 2026-08-13 defect — one roster carrying four or five promotions at once —
/// could be recreated by hand after <c>SplitAcademicGroupsPerLevel</c> repaired the data.</para>
/// </summary>
public static class AcademicGroupErrors
{
    public static Error NotFound(int groupId) => Error.NotFound(
        "AcademicGroups.NotFound",
        $"The academic group with Id = '{groupId}' was not found.");

    /// <summary>
    /// Les trois moitiés de l'identité d'un groupe — (année, niveau, numéro).
    /// </summary>
    /// <remarks>
    /// <para>⚠ Ce sont des refus adressés au <em>programmeur</em>, comme ceux d'un créneau : un
    /// appelant qui en omet un a écrit un bug, et la seule chose qui comptait est qu'il ne puisse
    /// plus le faire en silence. <c>Validation</c> et non <c>Problem</c>, parce qu'une demande
    /// malformée reste une demande malformée d'où qu'elle vienne.</para>
    ///
    /// <para>⚠ <b><see cref="RosterNeedsPromotion"/> n'est pas le refus de « Non réparti ».</b> Le
    /// panier est légitime et a sa propre fabrique, <c>AcademicGroup.AsUnassignedBucket</c> ; ce qui
    /// est refusé ici est de l'obtenir sans l'avoir voulu. Le refus adressé à l'<em>utilisateur</em>
    /// qui essaie de traiter le panier en groupe est
    /// <see cref="UnassignedRosterCannotBePartitioned"/>, plus bas.</para>
    /// </remarks>
    public static readonly Error RosterNeedsAcademicYear = Error.Validation(
        "AcademicGroups.RosterNeedsAcademicYear",
        "Un groupe appartient à une année universitaire : hors d'une année, ce n'est pas un groupe.");

    public static readonly Error RosterNeedsPromotion = Error.Validation(
        "AcademicGroups.RosterNeedsPromotion",
        "Un groupe suit le programme d'une seule promotion. Un groupe sans promotion est « Non "
        + "réparti », qui se demande explicitement et ne s'obtient pas en oubliant le niveau.");

    public static readonly Error RosterNeedsNumber = Error.Validation(
        "AcademicGroups.RosterNeedsNumber",
        "Un groupe porte un numéro dans sa promotion, à partir de 1 : le zéro est réservé à « Non "
        + "réparti », qui est hors de la numérotation.");

    /// <summary>
    /// The target roster belongs to a different academic year. Never a legitimate move: a registration
    /// <i>is</i> a year, so pointing it at another year's roster does not transfer the student, it
    /// makes the row describe two years at once.
    /// </summary>
    public static Error TargetGroupInAnotherYear(string groupLabel, string groupYear, string registrationYear) =>
        Error.Conflict(
            "AcademicGroups.TargetGroupInAnotherYear",
            $"« {groupLabel} » appartient à l'année {groupYear}, or cette inscription est celle de "
            + $"{registrationYear}. Un groupe n'existe que dans son année — choisissez un groupe de "
            + $"{registrationYear}.");

    /// <summary>
    /// The target roster belongs to a different promotion. A roster rotates through <i>one</i>
    /// promotion's stage set, so a 3rd-year sitting in a 5th-year roster would be planned into stages
    /// that are not his and counted against that promotion's service quotas.
    /// </summary>
    public static Error TargetGroupInAnotherLevel(string groupLabel, string groupLevel, string registrationLevel) =>
        Error.Conflict(
            "AcademicGroups.TargetGroupInAnotherLevel",
            $"« {groupLabel} » est un groupe de {groupLevel}, or cet étudiant est inscrit en "
            + $"{registrationLevel}. Un groupe suit le programme d'une seule promotion — choisissez un "
            + $"groupe de {registrationLevel}.");

    /// <summary>
    /// « Non réparti » — the one roster of a year that deliberately belongs to no promotion, because it
    /// holds every promotion's unassigned registrations at once (4,725 of them in 2025-2026).
    /// </summary>
    /// <remarks>
    /// ⚠ It is a holding pen, not a roster, and the two acts that would turn it into one are naming a
    /// partition on it and giving it a cohorte. Either makes every promotion in it move as a single
    /// body: a partition label pulls the whole bucket into <c>CohortProvisioner</c>, and a cohorte puts
    /// it in one service — which is how a partition assignment once reached 4,725 people.
    /// </remarks>
    public static Error UnassignedRosterCannotBePartitioned(string groupLabel) => Error.Validation(
        "AcademicGroups.UnassignedRosterCannotBePartitioned",
        $"« {groupLabel} » n'appartient à aucune promotion : il rassemble les inscriptions non "
        + "réparties de toutes les promotions de l'année. Lui donner une partition ferait tourner "
        + "toutes ces promotions ensemble. Répartissez d'abord ces étudiants dans des groupes.");

    /// <summary>
    /// Joining a roster is the act for a registration that has none; moving between two is a transfer,
    /// and the difference is not cosmetic. A transfer carries the student's running rotation across —
    /// interrupting the in-flight period, rehoming the future ones — and a first assignment has nothing
    /// to carry, so running it as a join would silently skip all of that.
    /// </summary>
    public static Error AlreadyInAGroup(string groupLabel) => Error.Conflict(
        "AcademicGroups.AlreadyInAGroup",
        $"Cet étudiant est déjà dans « {groupLabel} ». Utilisez un transfert pour le changer de groupe : "
        + "ses rotations en cours doivent suivre.");

    /// <summary>
    /// A registration whose year is over for the student — abandon, exclusion, diplôme. There is
    /// nothing to plan, and the roster's quota would count someone who will not come.
    /// </summary>
    public static Error CursusEndedCannotJoin(string status) => Error.Conflict(
        "AcademicGroups.CursusEndedCannotJoin",
        $"L'année de cet étudiant est close ({status}) : il n'y a pas de rotation à lui affecter. "
        + "Corrigez d'abord la décision de l'année si elle est erronée.");

    /// <summary>
    /// Two rosters of the same promotion cannot share a label — the label is what an admin reads.
    /// Across promotions they can: « Groupe 1 » exists in the 3rd year and in the 5th year at once,
    /// which is exactly how the faculty numbers and names them.
    /// </summary>
    public static Error DuplicateLabelInPromotion(string label, string promotion) => Error.Conflict(
        "AcademicGroups.DuplicateLabel",
        $"Un groupe nommé « {label} » existe déjà en {promotion} pour cette année.");

    /// <summary>
    /// « Vider le groupe » unhooks <c>Registration.AcademicGroupId</c> and nothing else — but an
    /// affectation hangs off the <i>cohorte</i>, so every one of them survives the act.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>The result is not an empty roster, it is a roster that reads empty.</b> The
    /// affectations stay in the roster's cohortes, their périodes stay on the chefs' worklists and
    /// stay counted against the services' occupancy, while the roster page shows 0 étudiants — and
    /// nothing on either screen says the two disagree.</para>
    ///
    /// <para>Worse, it is not undone by putting the students back: a re-découpage sends them to
    /// <i>other</i> rosters, <c>StudentAffectationService</c> keys its dedupe on (inscription,
    /// cohorte), and the new cohortes are not the old ones — so each student comes back with a second
    /// affectation for the same stage, counted twice everywhere.</para>
    /// </remarks>
    public static Error RosterHasAffectations(
        string groupLabel, int assignments, int periods) => Error.Conflict(
        "AcademicGroups.RosterHasAffectations",
        $"« {groupLabel} » n'est pas seulement une liste : ses étudiants tiennent {assignments} "
        + $"affectation(s) et {periods} période(s) de service, qui ne partiraient pas avec eux. Retirer "
        + "les étudiants sans elles laisserait ces affectations dans les cohortes du groupe — visibles "
        + "des chefs de service et comptées dans les effectifs — pour un groupe affiché vide. "
        + "Réinitialisez d'abord les cohortes du stage, ou confirmez la suppression des affectations.");

    /// <summary>
    /// The same act, once something has actually happened. Not forceable, and deliberately so: the
    /// destruction of marks and attendance has its own button (« Dépublier »), which names what it
    /// costs and asks twice. A roster-side act must never become the way round it.
    /// </summary>
    public static Error RosterAffectationsUnderway(
        string groupLabel, int periods, int started, int evaluated, int attendanceDays) => Error.Conflict(
        "AcademicGroups.RosterAffectationsUnderway",
        $"Les rotations de « {groupLabel} » sont engagées : sur {periods} période(s), {started} ont "
        + $"démarré, {evaluated} portent une évaluation et {attendanceDays} journée(s) de présence sont "
        + "enregistrées. Vider le groupe ne peut pas emporter cela. Arrêtez d'abord les rotations "
        + "— dépubliez la répartition du stage, qui indique précisément ce qui serait perdu — puis "
        + "revenez vider le groupe.");

    /// <summary>
    /// The promotion-wide « Vider les groupes de … ». Same refusal as the year-wide one, narrowed to
    /// the promotion the operator is looking at — and it names the promotion, because the whole point
    /// of the scope is that a refusal over <i>another</i> promotion's planning is not actionable.
    /// </summary>
    /// <remarks>
    /// Like its year-wide twin it offers no way to take the affectations along: that act is
    /// « Réinitialiser les cohortes », per stage, where the cost of each deletion is announced.
    /// </remarks>
    public static Error PromotionRostersHaveAffectations(
        string levelLabel, string yearLabel, int assignments, int periods) => Error.Conflict(
        "AcademicGroups.PromotionRostersHaveAffectations",
        $"Les groupes de {levelLabel} ({yearLabel}) portent {assignments} affectation(s) et {periods} "
        + "période(s) de service. Les vider laisserait tout cela en place, rattaché à des groupes "
        + "affichés vides. Réinitialisez les cohortes des stages de cette promotion — stage par stage, "
        + "où le coût de chaque suppression est annoncé — avant de vider ses groupes.");

    /// <summary>
    /// The year-wide « Vider toutes ». It deliberately offers <i>no</i> way to take the affectations
    /// with it: destroying every affectation of a year is not an act anybody means by "retirer les
    /// étudiants des groupes", and it has a proper owner per stage (« Réinitialiser les cohortes »).
    /// </summary>
    public static Error YearRostersHaveAffectations(
        string yearLabel, int assignments, int periods) => Error.Conflict(
        "AcademicGroups.YearRostersHaveAffectations",
        $"Les groupes de {yearLabel} portent {assignments} affectation(s) et {periods} période(s) de "
        + "service. Les vider laisserait tout cela en place, rattaché à des groupes affichés vides. "
        + "Réinitialisez les cohortes des stages concernés — stage par stage, où le coût de chaque "
        + "suppression est annoncé — avant de vider les groupes de l'année.");

    /// <summary>
    /// « Supprimer les groupes », while any roster in scope still holds a student.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>It names the scope and the number, because the fix depends on both.</b> The refusal
    /// used to be an inline <c>Error.Conflict</c> reading « One or more groups in this year have
    /// students assigned » — year-wide wording on an act the operator was running for one promotion,
    /// and no count. On a year holding four promotions it sent the operator to empty every one of
    /// them, which is exactly what happened on 2026-09-07 and is not a rule anybody meant.</para>
    ///
    /// <para>Deleting rosters is only ever needed to change their <i>number</i> or their
    /// <i>numbering</i> — emptied ones refill — so the order it enforces is: empty, then delete.</para>
    /// </remarks>
    /// <summary>
    /// « Découper cette promotion en N groupes » avec un N plus grand que l'effectif à répartir.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Les deux nombres sont nommés.</b> Un refus qui dirait « trop de groupes » renverrait
    /// l'opérateur deviner lequel des deux il a mal lu — le nombre qu'il a tapé, ou l'effectif qu'il
    /// croyait avoir. Et l'effectif annoncé est celui <i>à répartir</i>, signalements déduits : c'est
    /// le seul qui explique l'écart.
    /// </remarks>
    /// <remarks>
    /// ⚠ <b><c>Conflict</c>, pas <c>Problem</c>.</b> <c>ErrorType.Problem</c> n'est nommé nulle part
    /// dans <c>CustomResults.GetStatusCode</c> : il tombe sur le bras <c>_</c> et devient un
    /// <b>500</b>, sur lequel le client n'affiche que « Une erreur serveur est survenue ». La phrase
    /// ci-dessous, avec ses deux nombres, n'atteindrait jamais l'écran. Mesuré le 11/09/2026 en
    /// pilotant la coupe. C'est un conflit entre la demande et l'état, exactement comme
    /// <see cref="RostersHaveStudents"/> juste en dessous.
    /// </remarks>
    public static Error MoreGroupsThanStudents(int asked, int plannable, string promotionLabel) =>
        Error.Conflict(
            "AcademicGroups.MoreGroupsThanStudents",
            $"{asked} groupes demandés pour {promotionLabel}, qui ne compte que {plannable} "
            + "inscription(s) à répartir : chaque groupe doit recevoir au moins un étudiant. "
            + $"Demandez au plus {plannable} groupes, ou répartissez par taille.");

    public static Error RostersHaveStudents(string scopeLabel, int students, int rosters) =>
        Error.Conflict(
            "AcademicGroups.HasStudents",
            $"{rosters} groupe(s) de {scopeLabel} portent encore {students} étudiant(s). Videz-les "
            + "d'abord — « Vider la promotion » sur la promotion affichée, ou groupe par groupe — puis "
            + "supprimez-les. Supprimer un groupe habité détacherait ses étudiants sans le dire.");

    /// <summary>
    /// « Supprimer les groupes de … », once that promotion's rotations have actually run. The
    /// year-wide twin of this refusal named the year on an act scoped to one promotion, so it
    /// reported a cost belonging to promotions nobody was touching.
    /// </summary>
    public static Error PromotionRostersUnderway(
        string levelLabel, string yearLabel, int cohorts, int assignments, int periods,
        int started, int evaluated, int attendanceDays) => Error.Conflict(
        "AcademicGroups.PromotionRostersUnderway",
        $"Les rotations de {levelLabel} ({yearLabel}) sont engagées : {cohorts} cohorte(s), "
        + $"{assignments} affectation(s), {periods} période(s) dont {started} démarrée(s), "
        + $"{evaluated} évaluation(s) et {attendanceDays} journée(s) de présence. Supprimer les "
        + "groupes effacerait définitivement les évaluations et les présences. Dépubliez puis "
        + "réinitialisez les cohortes des stages de cette promotion — chacune de ces actions indique "
        + "ce qu'elle coûte — avant de supprimer ses groupes.");

    /// <summary>
    /// « Supprimer tous les groupes », once something in the year has actually run. The order this
    /// command enforces — empty the rosters, then delete them — normally leaves nothing to destroy by
    /// the time it runs; the guard stays because rosters emptied before that rule existed left their
    /// affectations behind, and this is the act that would have swept them away without a number.
    /// </summary>
    public static Error YearRostersUnderway(
        string yearLabel, int cohorts, int assignments, int periods,
        int started, int evaluated, int attendanceDays) => Error.Conflict(
        "AcademicGroups.YearRostersUnderway",
        $"{yearLabel} est engagée : {cohorts} cohorte(s), {assignments} affectation(s), {periods} "
        + $"période(s) dont {started} démarrée(s), {evaluated} évaluation(s) et {attendanceDays} "
        + "journée(s) de présence. Supprimer les groupes effacerait définitivement les évaluations et "
        + "les présences. Dépubliez puis réinitialisez les cohortes des stages concernés — chacune de "
        + "ces actions indique ce qu'elle coûte — avant de supprimer les groupes.");
}
