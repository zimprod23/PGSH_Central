using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Deliberation;

public static class DeliberationErrors
{
    public const string EmptySheetMessage =
        "Le fichier ne contient aucune ligne. Si véritablement aucun étudiant n'est concerné, "
        + "enregistrez la décision étudiant par étudiant plutôt que d'appliquer un fichier vide.";

    public static readonly Error NotAllowed = Error.Forbidden(
        "Deliberation.NotAllowed",
        "Seule la scolarité peut clôturer une année universitaire.");

    public static Error PromotionHasNoStudents(string scopeLabel, string yearLabel) => Error.NotFound(
        "Deliberation.PromotionHasNoStudents",
        $"Aucun étudiant inscrit en « {scopeLabel} » pour l'année {yearLabel}.");

    public static Error Rejected(int errorCount) => Error.Validation(
        "Deliberation.Rejected",
        $"{errorCount} ligne(s) en erreur — aucune décision n'a été enregistrée. "
        + "Corrigez le fichier et relancez la simulation.");

    /// <summary>
    /// The count the caller confirmed is not the count the plan computes. Almost always because a
    /// registration was created or a verdict recorded between the preview and the apply — which is
    /// exactly the case a checkbox would have waved through.
    /// </summary>
    public static Error DefaultsNotConfirmed(int expected, int? confirmed) => Error.Conflict(
        "Deliberation.DefaultsNotConfirmed",
        confirmed is null
            ? $"{expected} étudiant(s) seraient admis sans figurer dans le fichier. "
              + "Confirmez ce nombre avant d'appliquer."
            : $"Le nombre d'admissions par défaut a changé depuis la simulation "
              + $"({confirmed} confirmé(s), {expected} calculé(s) maintenant). Relancez la simulation.");

    public static readonly Error SheetUnreadable = Error.Validation(
        "Deliberation.SheetUnreadable",
        "Le fichier n'a pas pu être lu. Attendu : un classeur .xlsx généré depuis le canevas.");
}
