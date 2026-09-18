using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Reinscription;

public static class ReinscriptionErrors
{
    /// <summary>
    /// ⚠ The derived rollover's two years. It differs from the sheet's in what the départ year *is* —
    /// there it is the year the file closes, here it is the year whose verdicts are read to decide who
    /// moves up — so the sentence says that rather than borrowing the other one.
    /// </summary>
    public const string FromYearRequiredMessage =
        "L'année de départ est obligatoire : c'est dans ses inscriptions que sont lus les résultats "
        + "qui décident qui passe, qui redouble et qui sort.";

    public const string ToYearRequiredMessage =
        "L'année de destination est obligatoire : c'est elle qui reçoit les inscriptions créées.";

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
