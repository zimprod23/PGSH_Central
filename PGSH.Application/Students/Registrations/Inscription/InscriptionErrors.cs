using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Inscription;

public static class InscriptionErrors
{
    public const string EmptySheetMessage =
        "Le fichier ne contient aucune ligne. Pour inscrire un seul étudiant, un fichier d'une ligne "
        + "convient — mais un fichier vide n'inscrit personne.";

    public static readonly Error NotAllowed = Error.Forbidden(
        "Inscription.NotAllowed",
        "Seule la scolarité peut inscrire des étudiants.");

    /// <summary>
    /// « Retrait » and its kind are statuses the legacy base wore as levels. Nobody is inscribed into
    /// one: it has no stages, no cohortes and no rotation.
    /// </summary>
    public static Error NotAPromotion(string levelLabel) => Error.Validation(
        "Inscription.NotAPromotion",
        $"« {levelLabel} » n'est pas une année d'études : on ne peut y inscrire personne.");

    public static Error Rejected(int errorCount) => Error.Validation(
        "Inscription.Rejected",
        $"{errorCount} ligne(s) en erreur — aucun étudiant n'a été créé ni inscrit. "
        + "Corrigez le fichier et relancez la simulation.");

    /// <summary>
    /// The count the caller confirmed is not the count the plan computes.
    /// </summary>
    /// <remarks>
    /// ⚠ A boolean would not do, for a reason sharper than the déliberation's: this act creates
    /// <b>people</b>. A student row is an identity — a CNE, a numéro Apogée, an e-mail that
    /// <c>SyncUserMiddleware</c> will match a Keycloak login against — and there is no undo that puts
    /// a wrongly-created promotion back. The number the operator was shown is the only thing that
    /// catches a file edited between the preview and the apply.
    /// </remarks>
    public static Error CreationsNotConfirmed(int expected, int? confirmed) => Error.Conflict(
        "Inscription.CreationsNotConfirmed",
        confirmed is null
            ? $"{expected} étudiant(s) seraient créés dans la base. Confirmez ce nombre avant d'appliquer."
            : $"Le nombre d'étudiants à créer a changé depuis la simulation "
              + $"({confirmed} confirmé(s), {expected} calculé(s) maintenant). Relancez la simulation.");

    /// <summary>
    /// One named student could not be inscribed, in the row's own words.
    /// </summary>
    /// <remarks>
    /// The file path answers « N ligne(s) en erreur » because that is what a file needs. On a form
    /// that sentence names nothing the operator can act on, so the refusal carries the planner's own
    /// message — the same sentence the preview would have shown against that row — and the action as
    /// the code, so the client can key on it.
    /// </remarks>
    public static Error RowRefused(InscriptionAction action, string message) => Error.Validation(
        $"Inscription.{action}", message);

    public static readonly Error SheetUnreadable = Error.Validation(
        "Inscription.SheetUnreadable",
        "Le fichier n'a pas pu être lu. Attendu : un classeur .xlsx généré depuis le canevas.");
}
