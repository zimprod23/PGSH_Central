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
}
