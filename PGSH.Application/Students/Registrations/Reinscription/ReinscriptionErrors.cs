using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Reinscription;

public static class ReinscriptionErrors
{
    public static readonly Error NotAllowed = Error.Forbidden(
        "Reinscription.NotAllowed",
        "Seule la scolarité peut réinscrire une promotion.");

    public static readonly Error SameYear = Error.Validation(
        "Reinscription.SameYear",
        "L'année de destination doit être différente de l'année clôturée.");

    public static readonly Error TargetYearNotLater = Error.Validation(
        "Reinscription.TargetYearNotLater",
        "L'année de destination commence avant l'année clôturée — une réinscription va de l'avant.");

    public static Error PromotionHasNoStudents(string levelLabel, string yearLabel) => Error.NotFound(
        "Reinscription.PromotionHasNoStudents",
        $"Aucun étudiant inscrit en « {levelLabel} » pour l'année {yearLabel}.");
}
