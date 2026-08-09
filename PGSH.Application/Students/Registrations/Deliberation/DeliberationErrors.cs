using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Deliberation;

public static class DeliberationErrors
{
    public static readonly Error NotAllowed = Error.Forbidden(
        "Deliberation.NotAllowed",
        "Seule la scolarité peut clôturer une année universitaire.");

    public static Error PromotionHasNoStudents(string levelLabel, string yearLabel) => Error.NotFound(
        "Deliberation.PromotionHasNoStudents",
        $"Aucun étudiant inscrit en « {levelLabel} » pour l'année {yearLabel}.");

    public static Error Rejected(int errorCount) => Error.Validation(
        "Deliberation.Rejected",
        $"{errorCount} ligne(s) en erreur — aucune décision n'a été enregistrée. "
        + "Corrigez le fichier et relancez la simulation.");

    public static readonly Error SheetUnreadable = Error.Validation(
        "Deliberation.SheetUnreadable",
        "Le fichier n'a pas pu être lu. Attendu : un classeur .xlsx généré depuis le canevas.");
}
