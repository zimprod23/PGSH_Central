using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.BulkAssignment;

/// <summary>
/// The refusals of the act as a whole — never of one student, which is a row on the report.
/// </summary>
/// <remarks>
/// ⚠ The division is the point. A student who cannot be moved is reported and the others are still
/// written; a target roster that cannot receive anybody stops the act, because every line would
/// carry the same message and the fix is above them all.
/// </remarks>
internal static class BulkRosterAssignmentErrors
{
    public static readonly Error NotAllowed = Error.Forbidden(
        "RosterAssignment.NotAllowed",
        "Seule la scolarité peut affecter des étudiants à un groupe.");

    public static Error TargetInAnotherYear(string groupLabel, string yearLabel) => Error.Conflict(
        "RosterAssignment.TargetInAnotherYear",
        $"« {groupLabel} » n'appartient pas à {yearLabel} : un groupe est propre à une année "
        + "universitaire, et les inscriptions sélectionnées sont celles de cette année-là.");

    public static Error TargetIsUnassignedRoster(string groupLabel) => Error.Conflict(
        "RosterAssignment.TargetIsUnassignedRoster",
        $"« {groupLabel} » n'appartient à aucune promotion : il ne porte aucune cohorte, donc les "
        + "affectations des étudiants n'auraient nulle part où aller. Créez un groupe de la promotion "
        + "concernée.");

    public static Error NamesNobody => Error.Validation(
        "RosterAssignment.NamesNobody",
        "Aucun étudiant n'est désigné : indiquez au moins un groupe, une inscription ou un "
        + "identifiant (CNE ou Apogée).");

    /// <summary>
    /// ⚠ The guard that a checkbox cannot be. The act lands on students nobody typed the name of, so
    /// a registration created, transferred or evaluated between the preview and the click changes
    /// what runs without changing anything the operator saw. Both numbers are named because « ça a
    /// changé » is not something anybody can act on.
    /// </summary>
    public static Error CountMismatch(int confirmed, int actual) => Error.Conflict(
        "RosterAssignment.CountMismatch",
        $"La liste a changé depuis l'aperçu : {actual} étudiant(s) seraient affectés, alors que "
        + $"{confirmed} ont été confirmés. Relancez l'aperçu et vérifiez avant d'appliquer.");
}
